using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Mavrylo.Tests.TestSupport;

internal sealed class FakeEnvironment(string environmentName = "Development") : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Mavrylo.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = environmentName;
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
