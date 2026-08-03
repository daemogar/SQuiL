using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 8 (Phase 3 Postgres): the nested-object key-graph/stitch (<c>SQuiLKeyGraph</c>, Task 4)
/// and its INPUT-side key synthesis are dialect-neutral — both operate purely on the parsed
/// OUTPUT/INPUT <c>CodeBlock</c>s, above the <c>ISqlDialect</c> seam, regardless of whether the
/// header form is T-SQL <c>Declare</c>/<c>Use</c> (SQL Server, see
/// <see cref="SQuiL.Tests.NestedObjects.NestedOutputTests"/>) or a leading run of bare
/// <c>Create Temp Table</c> statements (SQLite, PostgreSQL).
///
/// These are SNAPSHOT verifications, not runtime round trips: unlike SQLite, PostgreSQL has no
/// in-process embeddable engine, so a live-DB proof needs a real server (Testcontainers,
/// tracked separately as TODO #8/3C). Each fixture below is the direct PostgreSQL twin — ported
/// to <c>Create Temp Table</c> + PG type spellings (<c>int</c>/<c>text</c>) — of the SQLite
/// runtime fixtures in <c>SQuiL.Tests/Sqlite/Queries/SqliteNestedOutput.sql</c> and
/// <c>SqliteNestedInput.sql</c> (exercised at runtime by
/// <see cref="SQuiL.Tests.Sqlite.SqliteNestedObjectTests"/>). Accepting these snapshots proves
/// PostgreSQL produces the SAME nested Response/Request reshaping — only the header/shred leaf
/// strings differ per dialect.
/// </summary>
public class PostgresNestedObjectTests
{
	/// <summary>
	/// OUTPUT nesting: <c>Returns_Order</c> (list root) with a <c>Returns_Line</c> (list child,
	/// FK <c>OrderID</c>). Only <c>Order</c> stays a top-level <c>Response</c> property; <c>Line</c>
	/// collapses into a settable <c>List&lt;...&gt;? Line</c> member on the <c>Order</c> record —
	/// the PostgreSQL twin of the SQLite runtime proof in
	/// <see cref="SQuiL.Tests.Sqlite.SqliteNestedObjectTests.Output_graph_stitches_lines_under_their_order"/>.
	/// </summary>
	[Fact]
	public System.Threading.Tasks.Task Nested_output_reshapes_line_under_order()
	{
		var name = nameof(Nested_output_reshapes_line_under_order);
		return TestHelper.VerifyPostgres([TestHelper.TestHeaderPostgres([name])], [$$"""
			--Name: {{name}}
			Create Temp Table Returns_Order (OrderID int Primary Key, CustomerName text);
			Create Temp Table Returns_Line (LineID int Primary Key, OrderID int, Product text, Qty int);
			Insert Into Returns_Order (OrderID, CustomerName) Values (1, 'Ada'), (2, 'Alan');
			Insert Into Returns_Line (LineID, OrderID, Product, Qty) Values (10, 1, 'Widget', 3), (11, 1, 'Gadget', 1), (12, 2, 'Gizmo', 5);
			Select OrderID, CustomerName From Returns_Order;
			Select LineID, OrderID, Product, Qty From Returns_Line;
			"""]);
	}

	/// <summary>
	/// INPUT nesting with key synthesis: <c>Param_Cart</c> (object root) with a <c>Params_Item</c>
	/// (list child, FK <c>CartID</c>) — neither PK/FK column is populated by the caller; the
	/// generator synthesizes <c>CartID</c>/<c>ItemID</c> at flatten time before shredding both temp
	/// tables through <c>json_to_recordset</c>. <c>Item</c> collapses into a settable
	/// <c>List&lt;...&gt;? Item { get; set; } = []</c> member on the <c>Cart</c> request record —
	/// the PostgreSQL twin of the SQLite runtime proof in
	/// <see cref="SQuiL.Tests.Sqlite.SqliteNestedObjectTests.Input_graph_synthesizes_keys_and_joins_children_to_parent"/>.
	/// </summary>
	[Fact]
	public System.Threading.Tasks.Task Nested_input_reshapes_item_under_cart()
	{
		var name = nameof(Nested_input_reshapes_item_under_cart);
		return TestHelper.VerifyPostgres([TestHelper.TestHeaderPostgres([name])], [$$"""
			--Name: {{name}}
			Create Temp Table Param_Cart (CartID int Primary Key, ShopperName text);
			Create Temp Table Params_Item (ItemID int Primary Key, CartID int, Product text);
			Create Temp Table Returns_CartLine (ShopperName text, Product text);
			Insert Into Returns_CartLine (ShopperName, Product) Select c.ShopperName, i.Product From Param_Cart c Join Params_Item i On i.CartID = c.CartID;
			Select ShopperName, Product From Returns_CartLine;
			"""]);
	}
}
