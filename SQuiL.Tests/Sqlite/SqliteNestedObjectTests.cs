using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 9 (Phase 3B): proves the two DIALECT-AGNOSTIC nested-object features work end-to-end for
/// SQLite — the in-memory key-stitch (OUTPUT graph) and the input key synthesis (INPUT graph).
/// Both are implemented once, above the dialect seam (<c>SQuiLKeyGraph</c> + the C# reader/flatten
/// emit), so nothing SQLite-specific should be required; this class is the runtime proof of that
/// claim over Microsoft.Data.Sqlite, mirroring <c>SQuiL.Tests/NestedObjects/**</c> (which snapshot
/// the SQL Server shapes) with an actual round trip.
///
/// Uses the same keep-alive shared-cache in-memory pattern as <see cref="SqliteRoundTripTests"/>:
/// a per-test uniquely-named <c>file:…?mode=memory&amp;cache=shared</c> database held open for the
/// test's lifetime, with the identical connection string handed to the generated context.
/// </summary>
public class SqliteNestedObjectTests
{
	private static (SqliteConnection keepAlive, ServiceProvider provider) Arrange(string dbName)
	{
		var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";

		var keepAlive = new SqliteConnection(connectionString);
		keepAlive.Open();

		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:SQuiLDatabase"] = connectionString,
		}).Build();

		ResetAddSQuiLGuard();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(config);
		services.AddSQuiL();
		return (keepAlive, services.BuildServiceProvider());
	}

	private static void ResetAddSQuiLGuard()
	{
		var property = typeof(SQuiLExtensions).GetProperty(
			nameof(SQuiLExtensions.IsLoaded),
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
		property!.SetValue(null, false);
	}

	/// <summary>
	/// OUTPUT nesting: <c>Returns_Order</c> (list root) with a <c>Returns_Line</c> (list child,
	/// FK <c>OrderID</c>). The generator reads both flat result sets and stitches each order's lines
	/// in memory by matching <c>Line.OrderID == Order.OrderID</c>. Order 1 has two lines, order 2
	/// has one — proving the PK⇄FK key-stitch groups children under the right parent for SQLite.
	/// </summary>
	[Fact]
	public async Task Output_graph_stitches_lines_under_their_order()
	{
		var (keepAlive, provider) = Arrange(nameof(Output_graph_stitches_lines_under_their_order));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteNestedOutputDataContext>();
		var result = await context.ProcessSqliteNestedOutputAsync(new SqliteNestedOutputRequest());

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);

		Assert.Equal(2, response!.Order!.Count);

		var order1 = Assert.Single(response.Order, o => o.OrderID == 1);
		Assert.Equal("Ada", order1.CustomerName);
		Assert.Equal(2, order1.Line!.Count);
		Assert.Contains(order1.Line, l => l.Product == "Widget" && l.Qty == 3);
		Assert.Contains(order1.Line, l => l.Product == "Gadget" && l.Qty == 1);

		var order2 = Assert.Single(response.Order, o => o.OrderID == 2);
		Assert.Equal("Alan", order2.CustomerName);
		var only = Assert.Single(order2.Line!);
		Assert.Equal("Gizmo", only.Product);
		Assert.Equal(5, only.Qty);
	}

	/// <summary>
	/// INPUT nesting with key synthesis: the caller supplies a <c>Cart</c> (object root) holding two
	/// nested <c>Item</c>s WITHOUT populating any PK/FK column. The generator synthesizes
	/// <c>CartID</c>/<c>ItemID</c> at flatten time and copies the parent's <c>CartID</c> into each
	/// child's FK before shredding both temp tables through <c>json_each</c>. The body then joins
	/// item→cart on the synthesized key; both joined rows carry the cart's shopper name — proving the
	/// synthesized keys actually stitched the two shredded tables together for SQLite.
	/// </summary>
	[Fact]
	public async Task Input_graph_synthesizes_keys_and_joins_children_to_parent()
	{
		var (keepAlive, provider) = Arrange(nameof(Input_graph_synthesizes_keys_and_joins_children_to_parent));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteNestedInputDataContext>();
		var result = await context.ProcessSqliteNestedInputAsync(new SqliteNestedInputRequest
		{
			Cart = new(0, "Ada")
			{
				Item =
				[
					new(0, 0, "Widget"),
					new(0, 0, "Gadget"),
				],
			},
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);

		Assert.Equal(2, response!.CartLine!.Count);
		Assert.All(response.CartLine, cl => Assert.Equal("Ada", cl.ShopperName));
		Assert.Contains(response.CartLine, cl => cl.Product == "Widget");
		Assert.Contains(response.CartLine, cl => cl.Product == "Gadget");
	}

	/// <summary>A reused embed instance is deduped: its Phone is sent once and joins both structures.</summary>
	[Fact]
	public async Task Embed_reused_instance_sends_its_children_once()
	{
		var (keepAlive, provider) = Arrange(nameof(Embed_reused_instance_sends_its_children_once));
		using var _ = keepAlive;

		var contact = new SQuiL.Tests.Sqlite.Models.Contact(1, "Ada") { Phone = [new(0, 0, "555-0100")] };
		var context = provider.GetRequiredService<SqliteEmbedWithChildInputDataContext>();
		var result = await context.ProcessSqliteEmbedWithChildInputAsync(new SqliteEmbedWithChildInputRequest
		{
			Structure = [new("A") { Contact = contact }, new("B") { Contact = contact }],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.PhoneLine!.Count);
		Assert.All(response.PhoneLine, pl => Assert.Equal("555-0100", pl.Number));
	}

	/// <summary>A second, different embed instance with children is rejected rather than silently dropped.</summary>
	[Fact]
	public async Task Embed_second_instance_with_children_throws()
	{
		var (keepAlive, provider) = Arrange(nameof(Embed_second_instance_with_children_throws));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteEmbedWithChildInputDataContext>();
		var request = new SqliteEmbedWithChildInputRequest
		{
			Structure =
			[
				new("A") { Contact = new(1, "Ada") { Phone = [new(0, 0, "555-0100")] } },
				new("B") { Contact = new(1, "Ada") { Phone = [new(0, 0, "555-0199")] } },
			],
		};

		var e = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ProcessSqliteEmbedWithChildInputAsync(request));
		Assert.Contains("Contact with ContactID '1' was supplied twice with nested children", e.Message);
	}

	/// <summary>A first childless sighting followed by a different instance WITH children is rejected.</summary>
	[Fact]
	public async Task Embed_later_instance_with_children_after_childless_first_throws()
	{
		var (keepAlive, provider) = Arrange(nameof(Embed_later_instance_with_children_after_childless_first_throws));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteEmbedWithChildInputDataContext>();
		var request = new SqliteEmbedWithChildInputRequest
		{
			Structure =
			[
				new("A") { Contact = new(1, "Ada") },
				new("B") { Contact = new(1, "Ada") { Phone = [new(0, 0, "555-0199")] } },
			],
		};

		var e = await Assert.ThrowsAsync<InvalidOperationException>(() => context.ProcessSqliteEmbedWithChildInputAsync(request));
		Assert.Contains("Contact with ContactID '1' was supplied twice with nested children", e.Message);
	}

	/// <summary>Output embed: each Building gets the Owner row its elided OwnerID points at.</summary>
	[Fact]
	public async Task Output_embed_stitches_the_lookup_into_each_container()
	{
		var (keepAlive, provider) = Arrange(nameof(Output_embed_stitches_the_lookup_into_each_container));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteEmbedOutputDataContext>();
		var result = await context.ProcessSqliteEmbedOutputAsync(new SqliteEmbedOutputRequest());

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(3, response!.Building!.Count);
		Assert.Equal("Ada", Assert.Single(response.Building, b => b.Title == "A").Owner!.Name);
		Assert.Equal("Alan", Assert.Single(response.Building, b => b.Title == "B").Owner!.Name);
		Assert.Equal(1, Assert.Single(response.Building, b => b.Title == "C").Owner!.OwnerID);
	}

	/// <summary>Output junction: Enrollment nests under Student and embeds its Course.</summary>
	[Fact]
	public async Task Output_junction_nests_enrollments_and_embeds_courses()
	{
		var (keepAlive, provider) = Arrange(nameof(Output_junction_nests_enrollments_and_embeds_courses));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteJunctionOutputDataContext>();
		var result = await context.ProcessSqliteJunctionOutputAsync(new SqliteJunctionOutputRequest());

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.Student!.Count);

		var ada = Assert.Single(response.Student, s => s.Name == "Ada");
		Assert.Equal(2, ada.Enrollment!.Count);
		Assert.Equal("Math", Assert.Single(ada.Enrollment, x => x.Grade == "A").Course!.Title);
		Assert.Equal("Art", Assert.Single(ada.Enrollment, x => x.Grade == "B").Course!.Title);

		var alan = Assert.Single(response.Student, s => s.Name == "Alan");
		Assert.Equal("Math", Assert.Single(alan.Enrollment!).Course!.Title);
	}

	/// <summary>Separate but identical instances (as a deserialized request has) dedup cleanly.</summary>
	[Fact]
	public async Task Chained_embed_identical_duplicate_instances_are_deduped()
	{
		var (keepAlive, provider) = Arrange(nameof(Chained_embed_identical_duplicate_instances_are_deduped));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteChainedEmbedInputDataContext>();
		var result = await context.ProcessSqliteChainedEmbedInputAsync(new SqliteChainedEmbedInputRequest
		{
			Office =
			[
				new("A") { Agent = new(1, "Ada") { Site = new(5, "Main") } },
				new("B") { Agent = new(1, "Ada") { Site = new(5, "Main") } },
			],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.OfficeStreet!.Count);
		Assert.All(response.OfficeStreet, os => Assert.Equal("Main", os.Street));
	}

	/// <summary>The same Agent row pointing at two different Addresses is a conflict, not a silent drop.</summary>
	[Fact]
	public async Task Chained_embed_duplicate_with_a_different_nested_key_throws()
	{
		var (keepAlive, provider) = Arrange(nameof(Chained_embed_duplicate_with_a_different_nested_key_throws));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteChainedEmbedInputDataContext>();
		var request = new SqliteChainedEmbedInputRequest
		{
			Office =
			[
				new("A") { Agent = new(1, "Ada") { Site = new(5, "Main") } },
				new("B") { Agent = new(1, "Ada") { Site = new(6, "Elm") } },
			],
		};

		var e = await Assert.ThrowsAsync<Exception>(() => context.ProcessSqliteChainedEmbedInputAsync(request));
		Assert.Contains("Conflicting values supplied for Agent with AgentID '1'", e.Message);
	}

	/// <summary>A second, different embed instance WITHOUT children is silently deduped.</summary>
	[Fact]
	public async Task Embed_second_instance_without_children_is_deduped()
	{
		var (keepAlive, provider) = Arrange(nameof(Embed_second_instance_without_children_is_deduped));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<SqliteEmbedWithChildInputDataContext>();
		var result = await context.ProcessSqliteEmbedWithChildInputAsync(new SqliteEmbedWithChildInputRequest
		{
			Structure =
			[
				new("A") { Contact = new(1, "Ada") { Phone = [new(0, 0, "555-0100")] } },
				new("B") { Contact = new(1, "Ada") },
			],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.PhoneLine!.Count);
	}
}
