﻿//HintName: GetActiveTermsForStudentEvaluationsDataContext.g.cs
// <auto-generated />

#nullable enable

using Microsoft.Data.SqlClient;

using SQuiL;

namespace CourseEvaluation.Application.Data;

partial class CourseEvaluationDataContext : SQuiLBaseDataContext
{
	public async Task<GetActiveTermsForStudentEvaluationsResponse> ProcessGetActiveTermsForStudentEvaluationsAsync(
		GetActiveTermsForStudentEvaluationsRequest request,
		CancellationToken cancellationToken = default!)
	{
		var builder = ConnectionStringBuilder("Warehouse");
		using SqlConnection connection = new(builder.ConnectionString);
		var command = connection.CreateCommand();
		
		List<SqlParameter> parameters = new()
		{
			new("EnvironmentName", System.Data.SqlDbType.VarChar, EnvironmentName.Length) { Value = EnvironmentName }, 
			new("Debug", System.Data.SqlDbType.Bit) { Value = EnvironmentName != "Production" }, 
			new("AsOfDate", System.Data.SqlDbType.Date) 
			{
				IsNullable = true,
				Value = request.AsOfDate ?? (object)System.DBNull.Value
			}
		};
		
		command.CommandText = Query(parameters);
		command.Parameters.AddRange(parameters.ToArray());
		
		await connection.OpenAsync(cancellationToken);
		
		GetActiveTermsForStudentEvaluationsResponse response = new();
		
		var isTerms = false;
		
		using var reader = await command.ExecuteReaderAsync(cancellationToken);
		
		do
		{
			var tableTag = reader.GetName(0);
			if(tableTag.StartsWith("__SQuiL__Table__Type__"))
			{
				switch (tableTag)
				{
					case "__SQuiL__Table__Type__Returns_Terms__":
					{
						isTerms = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						var indexTermCode = reader.GetOrdinal("TermCode");
						
						do
						{
							if (reader.GetString(0) == "Returns_Terms")
							{
								response.Terms.Add(new(
									reader.GetString(indexTermCode)));
							}
						}
						while (await reader.ReadAsync(cancellationToken));
						break;
					}
				}
			}
		}
		while (await reader.NextResultAsync(cancellationToken));
		
		if (!isTerms) throw new Exception("Expected return table `Terms`)");
		
		return response;
		
		string Query(List<SqlParameter> parameters) => $"""
		Declare @Returns_Terms table(
			[__SQuiL__Table__Type__Returns_Terms__] varchar(max) default('Returns_Terms'),
			TermCode varchar(10));
		
		Use [{builder.InitialCatalog}];
		
		If @Param_AsOfDate Is Null Set @Param_AsOfDate = GetDate();
		
		Insert Into @Returns_Terms(TermCode)
		Select		t.Term
		From		pub.Terms t
		Where		@Param_AsOfDate Between t.RegStartDate And IsNull(t.GradesDueDate, DateAdd(week, 1, t.EndDate))
		
		Select * From @Returns_Terms;
		
		""";
	}
}
