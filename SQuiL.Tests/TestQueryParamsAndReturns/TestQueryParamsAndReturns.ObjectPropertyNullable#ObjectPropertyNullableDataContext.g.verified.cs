﻿//HintName: ObjectPropertyNullableDataContext.g.cs
// <auto-generated />

#nullable enable

using Microsoft.Data.SqlClient;

using SQuiL;

namespace TestCase;

partial class TestQueryParamsAndReturnsDataContext : SQuiLBaseDataContext
{
	public async Task<ObjectPropertyNullableResponse> ProcessObjectPropertyNullableAsync(
		ObjectPropertyNullableRequest request,
		CancellationToken cancellationToken = default!)
	{
		var builder = ConnectionStringBuilder("SQuiLDatabase");
		using SqlConnection connection = new(builder.ConnectionString);
		var command = connection.CreateCommand();
		
		List<SqlParameter> parameters = new()
		{
			new("@EnvironmentName", System.Data.SqlDbType.VarChar, EnvironmentName.Length) { Value = EnvironmentName }, 
			new("@Debug", System.Data.SqlDbType.Bit) { Value = EnvironmentName != "Production" }, 
		};
		
		command.CommandText = Query(parameters);
		command.Parameters.AddRange(parameters.ToArray());
		
		await connection.OpenAsync(cancellationToken);
		
		ObjectPropertyNullableResponse response = new();
		
		var isStudent = false;
		var isParents = false;
		
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
						
						break;
					}
					case "__SQuiL__Table__Type__Return_Student__":
					{
						if (isStudent) throw new Exception(
							"Already returned value for `Student`");
						
						isStudent = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						if (response.Student is not null)
							throw new Exception("Student was already set.");
						
						if (reader.GetString(0) == "Return_Student")
						{
							response.Student = new(
								reader.GetInt32(reader.GetOrdinal("ID")),
								reader.IsDBNull(reader.GetOrdinal("FirstName")) ? default! : reader.GetString(reader.GetOrdinal("FirstName")),
								reader.GetString(reader.GetOrdinal("LastName")),
								reader.IsDBNull(reader.GetOrdinal("Age")) ? default! : reader.GetInt32(reader.GetOrdinal("Age")));
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
					case "__SQuiL__Table__Type__Returns_Parents__":
					{
						isParents = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						var indexID = reader.GetOrdinal("ID");
						var indexFirstName = reader.GetOrdinal("FirstName");
						var indexLastName = reader.GetOrdinal("LastName");
						var indexAge = reader.GetOrdinal("Age");
						
						do
						{
							if (reader.GetString(0) == "Returns_Parents")
							{
								response.Parents.Add(new(
									reader.GetInt32(indexID),
									reader.IsDBNull(indexFirstName) ? default! : reader.GetString(indexFirstName),
									reader.GetString(indexLastName),
									reader.IsDBNull(indexAge) ? default! : reader.GetInt32(indexAge)));
							}
						}
						while (await reader.ReadAsync(cancellationToken));
						break;
					}
				}
			}
		}
		while (await reader.NextResultAsync(cancellationToken));
		
		if (!isStudent) throw new Exception("Expected return object `Student`)");
		if (!isParents) throw new Exception("Expected return table `Parents`)");
		
		return response;
		
		string Query(List<SqlParameter> parameters) => $"""
		Declare @Return_Student table(
			[__SQuiL__Table__Type__Return_Student__] varchar(max) default('Return_Student'),
			[ID] int,
			[FirstName] varchar(100) Null,
			[LastName] varchar(100),
			[Age] int Null);
		
		Declare @Returns_Parents table(
			[__SQuiL__Table__Type__Returns_Parents__] varchar(max) default('Returns_Parents'),
			[ID] int,
			[FirstName] varchar(100) Null,
			[LastName] varchar(100),
			[Age] int Null);
		
		Use [{builder.InitialCatalog}];
		
		
		
		""";
	}
}
