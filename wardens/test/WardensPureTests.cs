// WardensPureTests.cs. The Unity-free helpers in wardens/src/WardensPure.cs.
//
//   dotnet test wardens/test/Wardens.Tests.csproj
//
// BuildLoopbackUrl is what the `timberbot` MCP tool calls for a GET. The playtest of 2026-09-10
// reported three Timberbot API bugs (a dropped tiles bound, pagination ignored, a name filter that
// matched nothing); all three were the passthrough appending "?format=json" after a query the
// agent had put inside `path`, so the last parameter's value became "55?format=json".

using Newtonsoft.Json.Linq;
using Wardens;
using Xunit;

public class WardensPureTests
{
    [Fact]
    public void Query_carried_in_path_is_kept_whole()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/tiles?x1=10&y1=40&x2=30&y2=55", null);
        Assert.Equal("http://127.0.0.1:8085/api/tiles?x1=10&y1=40&x2=30&y2=55&format=json", url);
    }

    [Fact]
    public void Explicit_query_overrides_the_path_entry()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/beavers?limit=5&offset=0", new JObject { ["offset"] = 10 });
        Assert.Equal("http://127.0.0.1:8085/api/beavers?limit=5&offset=10&format=json", url);
    }

    [Fact]
    public void Format_is_added_once_and_a_caller_s_format_wins()
    {
        Assert.Equal("http://127.0.0.1:8085/api/summary?format=json", WardensPure.BuildLoopbackUrl(8085, "/api/summary", null));
        Assert.Equal("http://127.0.0.1:8085/api/tiles?format=toon", WardensPure.BuildLoopbackUrl(8085, "/api/tiles?format=toon", null));
    }

    [Fact]
    public void Values_are_escaped()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/buildings", new JObject { ["name"] = "Charging Post" });
        Assert.Equal("http://127.0.0.1:8085/api/buildings?name=Charging%20Post&format=json", url);
    }

    [Fact]
    public void Empty_and_valueless_pairs_in_the_path_are_tolerated()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/summary?&verbose&", null);
        Assert.Equal("http://127.0.0.1:8085/api/summary?verbose=&format=json", url);
    }

    [Fact]
    public void Path_outside_api_is_refused()
    {
        Assert.Throws<System.ArgumentException>(() => WardensPure.BuildLoopbackUrl(8085, "/mcp", null));
        Assert.Throws<System.ArgumentException>(() => WardensPure.BuildLoopbackUrl(8085, null, null));
    }
}
