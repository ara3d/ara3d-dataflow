using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.DataFlowEngine.Tests;

/// <summary>A relation's identity is its plan hash; its payload never counts.</summary>
[TestFixture]
public class RelationValueTests
{
    [Test]
    public void Hash_depends_only_on_the_plan_hash()
    {
        var a = new RelationValue("(table \"db\" \"walls\")", "abc", Payload: new object());
        var b = new RelationValue("(table \"db\" \"walls\")", "abc");
        Assert.That(ValueHash.Compute(a), Is.EqualTo(ValueHash.Compute(b)));
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Different_plans_hash_differently()
        => Assert.That(ValueHash.Compute(new RelationValue("x", "1")), Is.Not.EqualTo(ValueHash.Compute(new RelationValue("x", "2"))));

    [Test]
    public void Encoding_is_tag_then_hash_text()
    {
        var bytes = ValueHash.Encode(new RelationValue("plan", "ab"));
        Assert.That(bytes[0], Is.EqualTo(0x06));
        Assert.That(bytes[1], Is.EqualTo(0x04));
        Assert.That(bytes.Length, Is.EqualTo(1 + 1 + 8 + 2));
    }

    [Test]
    public void Relation_ports_accept_only_relations()
    {
        Assert.That(PortType.Relation.Accepts(ValueKind.Relation), Is.True);
        Assert.That(PortType.Relation.Accepts(ValueKind.Table), Is.False);
        Assert.That(PortType.Table.Accepts(ValueKind.Relation), Is.False);
        Assert.That(PortType.Any.Accepts(ValueKind.Relation), Is.True);
    }
}
