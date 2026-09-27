using System.Reflection;
using Xunit;

namespace ZeroAlloc.Rest.Tools.Tests;

// The STJ source generator and the union variant names use a keyword type's CLR name: Int32 for
// int. The map is keyed by the TypeRef primitives, so a new keyword primitive needs an entry.
public class TypeRefTests
{
    public static TheoryData<string, string> KeywordPrimitives()
    {
        var data = new TheoryData<string, string>();
        foreach (var property in typeof(TypeRef).GetProperties(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (property.GetValue(null) is TypeRef { Kind: TypeRefKind.Primitive } type && !type.Name.Contains('.', StringComparison.Ordinal))
                data.Add(type.Name.TrimEnd('[', ']'), property.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(KeywordPrimitives))]
    public void EveryKeywordPrimitive_HasItsClrName(string keyword, string primitive)
        => Assert.False(TypeRef.ClrName(keyword) is null, $"TypeRef.{primitive} ({keyword}) has no CLR name.");

    [Theory]
    [InlineData("int", typeof(int))]
    [InlineData("long", typeof(long))]
    [InlineData("float", typeof(float))]
    [InlineData("double", typeof(double))]
    [InlineData("decimal", typeof(decimal))]
    [InlineData("bool", typeof(bool))]
    [InlineData("string", typeof(string))]
    [InlineData("byte", typeof(byte))]
    public void ClrName_IsTheRuntimeTypesName(string keyword, Type type)
        => Assert.Equal(type.Name, TypeRef.ClrName(keyword));

    [Fact]
    public void ClrName_OfANonKeyword_IsNull()
        => Assert.Null(TypeRef.ClrName("global::System.Guid"));
}
