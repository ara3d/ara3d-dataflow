using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.DataFlowEngine.Tests;

/// <summary>A database's identity is the content hash of its file; path and payload never count.</summary>
[TestFixture]
public class DatabaseValueTests
{
    [Test]
    public void Hash_depends_only_on_the_content_hash()
    {
        var a = new DatabaseValue("a.duckdb", "abc", Payload: new object());
        var b = new DatabaseValue("elsewhere.duckdb", "abc");
        Assert.That(ValueHash.Compute(a), Is.EqualTo(ValueHash.Compute(b)));
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void A_changed_file_is_a_new_value()
        => Assert.That(ValueHash.Compute(new DatabaseValue("a", "1")), Is.Not.EqualTo(ValueHash.Compute(new DatabaseValue("a", "2"))));

    [Test]
    public void Encoding_is_tag_then_hash_text()
    {
        var bytes = ValueHash.Encode(new DatabaseValue("p", "ab"));
        Assert.That(bytes[0], Is.EqualTo(0x07));
        Assert.That(bytes[1], Is.EqualTo(0x04));
        Assert.That(bytes.Length, Is.EqualTo(1 + 1 + 8 + 2));
    }

    [Test]
    public void Database_ports_accept_only_databases()
    {
        Assert.That(PortType.Database.Accepts(ValueKind.Database), Is.True);
        Assert.That(PortType.Database.Accepts(ValueKind.Text), Is.False);
        Assert.That(PortType.Text.Accepts(ValueKind.Database), Is.False);
        Assert.That(PortType.Any.Accepts(ValueKind.Database), Is.True);
    }
}
