using Ara3D.DataFlowEngine.Expressions;
using Ara3D.DataFlowEngine.Expressions.Parsing;
using static Ara3D.DataFlowEngine.Expressions.Tests.TestHelpers;

namespace Ara3D.DataFlowEngine.Expressions.Tests;

/// <summary>isnull(x), the one test for a missing value, and the in / not in membership operator.</summary>
[TestFixture]
public class MissingAndMembershipTests
{
    [TestCase("isnull(ni)", true)]
    [TestCase("isnull(nt)", true)]
    [TestCase("isnull(null)", true)]
    [TestCase("isnull(i)", false)]
    [TestCase("isnull(n)", false)]
    [TestCase("isnull(t)", false)]
    [TestCase("isnull(b)", false)]
    [TestCase("isnull(ni + 1)", true)]
    [TestCase("not isnull(ni)", false)]
    [TestCase("isnull(ni) ? 'missing' == 'missing' : false", true)]
    public void IsNullIsNeverNull(string text, bool expected)
        => Assert.That(Eval(text), Is.EqualTo(new BooleanScalar(expected)));

    [Test]
    public void IsNullIsBoolean()
        => Assert.That(TypeOf("isnull(ni)"), Is.EqualTo(ScalarType.Boolean));

    [TestCase("isnull()")]
    [TestCase("isnull(i, n)")]
    public void IsNullTakesOneArgument(string text)
        => Assert.That(FirstTypeError(text).Message, Does.Contain("expects 1 argument"));

    [TestCase("t in ('x', 'abc')", true)]
    [TestCase("t in ('x', 'y')", false)]
    [TestCase("t in ('ABC')", false)]
    [TestCase("t not in ('x', 'y')", true)]
    [TestCase("t not in ('abc')", false)]
    [TestCase("i in (1, 2, 3)", true)]
    [TestCase("i in (3.0)", true)]
    [TestCase("n in (2.5, 7)", true)]
    [TestCase("i in (-3, 4)", false)]
    [TestCase("-i in (-3, 4)", true)]
    [TestCase("b in (true)", true)]
    [TestCase("i + 1 in (4)", true)]
    [TestCase("t & 'd' in ('abcd')", true)]
    [TestCase("i in (3) and t in ('abc')", true)]
    public void Membership(string text, bool expected)
        => Assert.That(Eval(text), Is.EqualTo(new BooleanScalar(expected)));

    [TestCase("ni in (1, 2)")]
    [TestCase("ni not in (1, 2)")]
    [TestCase("nt in ('abc')")]
    public void MembershipOfNullIsNull(string text)
        => Assert.That(Eval(text), Is.Null);

    [Test]
    public void MembershipIsBoolean()
        => Assert.That(TypeOf("t in ('a')"), Is.EqualTo(ScalarType.Boolean));

    [TestCase("t in (1)")]
    [TestCase("i in ('3')")]
    [TestCase("b in (1)")]
    public void MembershipItemsMustBeComparable(string text)
        => Assert.That(FirstTypeError(text).Message, Does.Contain("'in' item"));

    [TestCase("i in (n)")]
    [TestCase("i in (null)")]
    [TestCase("i in (1 + 1)")]
    [TestCase("t in (lower('A'))")]
    public void MembershipItemsMustBeNonNullLiterals(string text)
        => Assert.That(ParseError(text).Message, Does.Contain("non-null literals"));

    [TestCase("i in ()")]
    [TestCase("i in 1, 2")]
    [TestCase("i in (1, 2")]
    [TestCase("i not (1)")]
    public void MalformedListsDoNotParse(string text)
        => ParseError(text);

    [Test]
    public void InIsAKeywordButAQuotedColumnMayUseIt()
    {
        ParseError("in + 1");
        var errors = new List<ExprError>();
        Assert.That(Parser.Parse("[in] + 1", errors), Is.Not.Null);
    }

    private static ExprError ParseError(string text)
    {
        var errors = new List<ExprError>();
        Assert.That(Parser.Parse(text, errors), Is.Null, text);
        Assert.That(errors, Is.Not.Empty, text);
        return errors[0];
    }
}
