﻿//HintName: QueryHasErrorObjectDataContext.g.cs
// <auto-generated />

#nullable enable

using Microsoft.Data.SqlClient;

using SQuiL;

namespace TestCase;

partial class QueryHasErrorObjectDataContext : SQuiLBaseDataContext
{
	public async Task<SQuiLResultType<QueryHasErrorObjectResponse>> ProcessQueryHasErrorObjectAsync(
		QueryHasErrorObjectRequest request,
		CancellationToken cancellationToken = default!)
	{
		var builder = ConnectionStringBuilder("SQuiLDatabase");
		using SqlConnection connection = new(builder.ConnectionString);
		var command = connection.CreateCommand();
		
		List<SqlParameter> parameters = new()
		{
			new("@EnvironmentName", System.Data.SqlDbType.VarChar, EnvironmentName.Length) { Value = EnvironmentName }, 
			new("@Debug", System.Data.SqlDbType.Bit) { Value = EnvironmentName != "Production" }, 
			new("@Param_Elapsed", System.Data.SqlDbType.BigInt) 
			{
				IsNullable = true,
				Value = request.Elapsed ?? (object)System.DBNull.Value
			}
		};
		
		command.CommandText = Query(parameters);
		command.Parameters.AddRange(parameters.ToArray());
		
		await connection.OpenAsync(cancellationToken);
		
		QueryHasErrorObjectResponse response = new();
		
		var isSampleID = false;
		var isSampleEntity = false;
		var isSamples = false;
		
		List<SQuiLError> errors = [];
		
		using var reader = await command.ExecuteReaderAsync(cancellationToken);
		
		do
		{
			var tableTag = reader.GetName(0);
			if(tableTag.StartsWith("__SQuiL__Table__Type__"))
			{
				switch (tableTag)
				{
					case "__SQuiL__Table__Type__Error__":
					{
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						var indexNumber = reader.GetOrdinal("Number");
						var indexSeverity = reader.GetOrdinal("Severity");
						var indexState = reader.GetOrdinal("State");
						var indexLine = reader.GetOrdinal("Line");
						var indexProcedure = reader.GetOrdinal("Procedure");
						var indexMessage = reader.GetOrdinal("Message");
						
						do
						{
							if (reader.GetString(0) == "Error")
							{
								errors.Add(new(
									reader.GetInt32(indexNumber),
									reader.GetInt32(indexSeverity),
									reader.GetInt32(indexState),
									reader.GetInt32(indexLine),
									reader.GetString(indexProcedure),
									reader.GetString(indexMessage)));
							}
						}
						while (await reader.ReadAsync(cancellationToken));
						
						break;
					}
					case "__SQuiL__Table__Type__Return_SampleID__":
					{
						if (isSampleID) throw new Exception(
							"Already returned value for `SampleID`");
						
						isSampleID = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						response.SampleID = !reader.IsDBNull(1) ? reader.GetInt32(1) : null;
						break;
					}
					case "__SQuiL__Table__Type__Return_SampleEntity__":
					{
						if (isSampleEntity) throw new Exception(
							"Already returned value for `SampleEntity`");
						
						isSampleEntity = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						if (response.SampleEntity is not null)
							throw new Exception("SampleEntity was already set.");
						
						if (reader.GetString(0) == "Return_SampleEntity")
						{
							response.SampleEntity = new(
								reader.GetInt32(reader.GetOrdinal("ID")));
						}
						else
						{
							continue;
						}
						
						if (await reader.ReadAsync(cancellationToken))
							throw new Exception(
								"Return object results in more than one object. Consider using a return table instead.");
						
						break;
					}
					case "__SQuiL__Table__Type__Returns_Samples__":
					{
						isSamples = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						var indexID = reader.GetOrdinal("ID");
						
						do
						{
							if (reader.GetString(0) == "Returns_Samples")
							{
								response.Samples.Add(new(
									reader.GetInt32(indexID)));
							}
						}
						while (await reader.ReadAsync(cancellationToken));
						break;
					}
				}
			}
		}
		while (await reader.NextResultAsync(cancellationToken));
		
		if (!isSampleID) throw new Exception("Expected return scaler `SampleID`)");
		if (!isSampleEntity) throw new Exception("Expected return object `SampleEntity`)");
		if (!isSamples) throw new Exception("Expected return table `Samples`)");
		
		if(errors.Count == 0)
			return new(response);
		
		return new(errors);
		
		string Query(List<SqlParameter> parameters) => $"""
		Declare @Return_SampleID int;
		
		Declare @Return_SampleEntity table(
			[__SQuiL__Table__Type__Return_SampleEntity__] varchar(max) default('Return_SampleEntity'),
			[ID] int);
		
		Declare @Returns_Samples table(
			[__SQuiL__Table__Type__Returns_Samples__] varchar(max) default('Returns_Samples'),
			[ID] int);
		
		Declare @Error table(
			[__SQuiL__Table__Type__Error__] varchar(max) default('Error'),
			[Number] int,
			[Severity] int,
			[State] int,
			[Line] int,
			[Procedure] varchar(max),
			[Message] varchar(max));
		
		Use [{builder.InitialCatalog}];
		
		
		
		Select 'Return_SampleID' As [__SQuiL__Table__Type__Return_SampleID__], @Return_SampleID;
		
		""";
	}
}
