using System;

namespace DeskSnip.App;

/// <summary>画布保存样式后通知控制器；样式状态本身留在各画布，避免测试或窗口之间共享可变字典。</summary>
internal static class AnnotationStylePersistence
{
    internal static event Action<string>? Changed;
    internal static void Publish(string value) => Changed?.Invoke(value);
}
