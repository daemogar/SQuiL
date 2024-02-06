using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL;

using SquilParser.Simple;

Console.WriteLine("Hello, World!");

ConfigurationBuilder builder = new();
builder.AddInMemoryCollection(new Dictionary<string, string?>
{
  ["ConnectionStrings:SQuiLDatabase"] = "Data Source=sqldev.intranet.southern.edu;Initial Catalog=UnitTesting;Integrated Security=True;App=TestCondition;Connect Timeout=120"
});

ServiceCollection services = new();

services.AddSingleton<IConfiguration>(builder.Build());
services.AddSingleton<TestDataContext>();

var provider = services.BuildServiceProvider();

var context = provider.GetRequiredService<TestDataContext>();
var response = await context.ProcessQueriesExampleAsync(new()
{
  
});

Console.WriteLine(response);
Console.ReadKey();

namespace SquilParser.Simple
{
  [SQuiL(QueryFiles.QueriesExample)]
  public partial class TestDataContext(IConfiguration configuration) : SQuiLBaseDataContext(configuration) { }
}