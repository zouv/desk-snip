[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing
$assetDirectory = Assert-ManagedPath (Join-Path $Root 'src/DeskSnip.App/Assets')
$sourcePath = Join-Path $assetDirectory 'DeskSnipIcon.xaml'
$source = [IO.File]::OpenRead($sourcePath)
try { $drawing = [Windows.Markup.XamlReader]::Load($source) }
finally { $source.Dispose() }

$sizes = @(16, 20, 24, 28, 32, 40, 48, 64, 128, 256)
function Convert-AssetColor($color) {
    return [Drawing.Color]::FromArgb($color.A, $color.R, $color.G, $color.B)
}
function New-AssetBrush($brush, $bounds) {
    if ($brush -is [Windows.Media.SolidColorBrush]) {
        return [Drawing.SolidBrush]::new((Convert-AssetColor $brush.Color))
    }
    if ($brush -is [Windows.Media.LinearGradientBrush]) {
        $start = [Drawing.PointF]::new(($bounds.X + $brush.StartPoint.X * $bounds.Width), ($bounds.Y + $brush.StartPoint.Y * $bounds.Height))
        $end = [Drawing.PointF]::new(($bounds.X + $brush.EndPoint.X * $bounds.Width), ($bounds.Y + $brush.EndPoint.Y * $bounds.Height))
        $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new($start, $end, [Drawing.Color]::Blue, [Drawing.Color]::Blue)
        $blend = [Drawing.Drawing2D.ColorBlend]::new($brush.GradientStops.Count)
        $blend.Colors = [Drawing.Color[]]@($brush.GradientStops | ForEach-Object { Convert-AssetColor $_.Color })
        $blend.Positions = [single[]]@($brush.GradientStops | ForEach-Object { $_.Offset })
        $gradient.InterpolationColors = $blend
        return $gradient
    }
    throw 'Unsupported icon brush.'
}
function Draw-AssetNode($graphics, $node) {
    if ($node -is [Windows.Media.DrawingGroup]) {
        $state = $graphics.Save()
        try {
            if ($node.Transform) {
                $value = $node.Transform.Value
                $matrix = [Drawing.Drawing2D.Matrix]::new($value.M11, $value.M12, $value.M21, $value.M22, $value.OffsetX, $value.OffsetY)
                try { $graphics.MultiplyTransform($matrix) } finally { $matrix.Dispose() }
            }
            foreach ($child in $node.Children) { Draw-AssetNode $graphics $child }
        }
        finally { $graphics.Restore($state) }
        return
    }
    if ($node -isnot [Windows.Media.GeometryDrawing]) { throw 'Unsupported icon drawing.' }
    $geometry = $node.Geometry.GetFlattenedPathGeometry(0.05, [Windows.Media.ToleranceType]::Absolute)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $path.FillMode = if ($geometry.FillRule -eq [Windows.Media.FillRule]::Nonzero) { [Drawing.Drawing2D.FillMode]::Winding } else { [Drawing.Drawing2D.FillMode]::Alternate }
        foreach ($figure in $geometry.Figures) {
            $points = @([Drawing.PointF]::new($figure.StartPoint.X, $figure.StartPoint.Y))
            foreach ($segment in $figure.Segments) {
                if ($segment -is [Windows.Media.PolyLineSegment]) {
                    foreach ($point in $segment.Points) { $points += [Drawing.PointF]::new($point.X, $point.Y) }
                }
                elseif ($segment -is [Windows.Media.LineSegment]) { $points += [Drawing.PointF]::new($segment.Point.X, $segment.Point.Y) }
                else { throw 'Icon geometry was not flattened.' }
            }
            $path.StartFigure()
            if ($points.Count -ge 2) { $path.AddLines([Drawing.PointF[]]$points) }
            if ($figure.IsClosed) { $path.CloseFigure() }
        }
        if ($node.Brush) {
            $fill = New-AssetBrush $node.Brush $node.Geometry.Bounds
            try { $graphics.FillPath($fill, $path) } finally { $fill.Dispose() }
        }
        if ($node.Pen) {
            $stroke = [Drawing.Pen]::new((Convert-AssetColor $node.Pen.Brush.Color), $node.Pen.Thickness)
            $stroke.StartCap = [Drawing.Drawing2D.LineCap]::Round; $stroke.EndCap = [Drawing.Drawing2D.LineCap]::Round; $stroke.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
            try { $graphics.DrawPath($stroke, $path) } finally { $stroke.Dispose() }
        }
    }
    finally { $path.Dispose() }
}
$frames = @()
foreach ($size in $sizes) {
    # GDI rasterization stays deterministic when an RDP desktop is disconnected.
    $large = [Drawing.Bitmap]::new(($size * 4), ($size * 4), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($large)
    $result = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $output = [Drawing.Graphics]::FromImage($result)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size * 4 / 256), ($size * 4 / 256))
        Draw-AssetNode $graphics $drawing.Drawing
        $output.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $output.DrawImage($large, 0, 0, $size, $size)
        if ($result.GetPixel(($size / 2), ($size / 2)).A -eq 0) { throw 'Icon rendering is empty.' }
        $memory = New-Object IO.MemoryStream
        try { $result.Save($memory, [Drawing.Imaging.ImageFormat]::Png); $frames += ,$memory.ToArray() }
        finally { $memory.Dispose() }
    }
    finally { $output.Dispose(); $result.Dispose(); $graphics.Dispose(); $large.Dispose() }
}

$iconPath = Assert-ManagedPath (Join-Path $assetDirectory 'DeskSnip.ico')
$stream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'DeskSnip.png'), $frames[-1])
Write-Ok 'Generated the blue DeskSnip icon in 10 sizes (16 to 256 pixels).'
