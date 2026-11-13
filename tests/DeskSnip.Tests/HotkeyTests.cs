using System;
using DeskSnip.App.Windows;
using Xunit;

namespace DeskSnip.Tests;

public class HotkeyTests
{
    [Fact] public void EquivalentKeyOrderHasSameIdentity() => Assert.Equal(Hotkeys.Parse("Ctrl+Alt+A"), Hotkeys.Parse("alt + CTRL + a"));
    [Theory]
    [InlineData("A")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Alt+Bogus")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Ctrl++A")]
    public void InvalidKeysAreRejected(string key) => Assert.Throws<ArgumentException>(() => Hotkeys.Parse(key));
}
