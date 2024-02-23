using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL;

using SquilParser.Simple;

Console.WriteLine("Hello, World!");

ConfigurationBuilder builder = new();
builder.AddInMemoryCollection(new Dictionary<string, string?>
{
	["ConnectionStrings:SQuiLDatabase"] = "Data Source=sqldev.intranet.southern.edu;Initial Catalog=UnitTesting;Integrated Security=True;App=TestCondition;Connect Timeout=120;TrustServerCertificate=True;",
	["ConnectionStrings:ExampleOne"] = "Data Source=sqldev.intranet.southern.edu;Initial Catalog=UnitTesting;Integrated Security=True;App=TestCondition;Connect Timeout=120;TrustServerCertificate=True;",
	["ConnectionStrings:ExampleTwo"] = "Data Source=sqldev.intranet.southern.edu;Initial Catalog=UnitTesting;Integrated Security=True;App=TestCondition;Connect Timeout=120;TrustServerCertificate=True;"
});

ServiceCollection services = new();

services.AddSingleton<IConfiguration>(builder.Build());
services.AddSingleton<TestDataContext>();

var provider = services.BuildServiceProvider();

var context = provider.GetRequiredService<TestDataContext>();
var response = await context.ProcessQueriesExample1Async(new()
{

});

Console.WriteLine(response);
Console.ReadKey();

namespace SquilParser.Simple
{
	[SQuiL(QueryFiles.QueriesExample1, setting: "ExampleOne")]
	[SQuiL(QueryFiles.QueriesExample2, setting: "ExampleTwo")]
	public partial class TestDataContext(IConfiguration configuration) : SQuiLBaseDataContext(configuration) { }

	//[SQuiL(QueryFiles.QueriesExample1, setting: "ExampleOne")]
	//public partial class TestDataContext : SQuiLBaseDataContext { }
}