using Capta.Services;

namespace Capta.Tests;

public class IntegraManagementTests
{
    [Fact]
    public void Capta_on_Integras_list_is_managed()
    {
        const string json = """
            [ { "Id": "ocula", "Version": "1.0.0", "Kind": 1 },
              { "Id": "capta", "Version": "1.1.2", "Kind": 3, "Origin": "" } ]
            """;
        Assert.True(IntegraManagement.Lists(json));
    }

    [Fact]
    public void The_property_names_are_not_case_sensitive()
        => Assert.True(IntegraManagement.Lists("""[ { "id": "Capta" } ]"""));

    [Fact]
    public void An_app_Integra_only_found_installed_is_not_managed()
        => Assert.False(IntegraManagement.Lists("""[ { "Id": "capta", "Origin": "detected" } ]"""));

    [Fact]
    public void Other_apps_only_is_not_managed()
        => Assert.False(IntegraManagement.Lists("""[ { "Id": "ocula" }, { "Id": "porta" } ]"""));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[ 1, \"capta\", null ]")]
    [InlineData("""[ { "Id": 5 } ]""")]
    public void Anything_else_is_not_managed(string json)
        => Assert.False(IntegraManagement.Lists(json));
}
