using NUnit.Framework;

// The lobby hands one of these addresses to a friend, and handing over the
// wrong one costs both people the twenty minutes it takes to stop trusting it.
// The ordering that decides which one is offered first rests entirely on this
// call, so this is the part worth pinning down.
public sealed class LanAddressProviderTests
{
    [Test]
    public void LooksLikeOverlay_SeparatesRealAdaptersFromVirtualOnes()
    {
        Assert.That(LanAddressProvider.LooksLikeOverlay("Radmin VPN"), Is.True);
        Assert.That(LanAddressProvider.LooksLikeOverlay("Hamachi Network Interface"), Is.True);
        Assert.That(LanAddressProvider.LooksLikeOverlay("ZeroTier One [abc]"), Is.True);
        Assert.That(LanAddressProvider.LooksLikeOverlay("VMware Virtual Ethernet Adapter"), Is.True);

        Assert.That(LanAddressProvider.LooksLikeOverlay("Ethernet"), Is.False);
        Assert.That(LanAddressProvider.LooksLikeOverlay("Wi-Fi"), Is.False);
        Assert.That(LanAddressProvider.LooksLikeOverlay("Ethernet 2"), Is.False);
        Assert.That(LanAddressProvider.LooksLikeOverlay(string.Empty), Is.False);
        Assert.That(LanAddressProvider.LooksLikeOverlay(null), Is.False);
    }

    // Whatever the machine running the tests happens to have plugged in, the
    // list has to agree with itself: nothing empty, nothing loopback, and the
    // single answer is the first of the many.
    [Test]
    public void GetAll_AgreesWithGet()
    {
        var options = LanAddressProvider.GetAll();

        Assert.That(options, Is.Not.Null);

        foreach (var option in options)
        {
            Assert.That(option.Address, Is.Not.Empty);
            Assert.That(option.Address, Does.Not.StartWith("127."));
            Assert.That(option.Address, Does.Not.StartWith("169.254."));
            Assert.That(option.Label, Is.Not.Empty);
        }

        Assert.That(
            LanAddressProvider.Get(),
            Is.EqualTo(options.Count > 0 ? options[0].Address : string.Empty));
    }
}
