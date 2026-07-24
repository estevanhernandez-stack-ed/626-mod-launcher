// tests/ModManager.Tests/Plugins/AuthorizedSendContractTests.cs
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using ModManager.Plugins.Abstractions;
using Xunit;

public class AuthorizedSendContractTests
{
    [Fact]
    public void IAuthorizedSend_has_expected_shape()
    {
        var m = typeof(IAuthorizedSend).GetMethod("SendAuthorizedAsync")!;
        Assert.Equal(typeof(Task<HttpResponseMessage>), m.ReturnType);
        var p = m.GetParameters();
        Assert.Equal(typeof(HttpRequestMessage), p[0].ParameterType);
        Assert.Equal(typeof(string), p[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), p[2].ParameterType);
    }

    [Fact]
    public void GetCredential_still_present_for_abi_but_obsolete()
    {
        // Removing it would MissingMethodException the shipped 0.10.0 plugin at load.
        var m = typeof(IPluginHostServices).GetMethod("GetCredential")!;
        Assert.NotNull(m);
        Assert.NotNull(m.GetCustomAttribute<System.ObsoleteAttribute>());
    }
}
