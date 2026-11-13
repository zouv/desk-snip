using System;
using System.Collections.Generic;
using System.IO;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class LocalRecoveryTests
{
    [Fact] public void MalformedHistoryCannotIntroducePathsOrUnsupportedKinds()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            AtomicFile.WriteJson(Path.Combine(root, "history.json"), new List<HistoryEntry>
            {
                new HistoryEntry { Id = Guid.NewGuid().ToString("N"), Kind = "unknown", FileName = "../../outside.txt" },
                new HistoryEntry { Id = Guid.NewGuid().ToString("N"), Kind = "Image", FileName = "../../outside.png" }
            });
            Assert.Empty(new LocalStore(root).History);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
