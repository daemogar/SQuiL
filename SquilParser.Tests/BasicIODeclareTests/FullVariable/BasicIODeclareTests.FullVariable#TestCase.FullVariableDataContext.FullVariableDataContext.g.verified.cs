﻿//HintName: TestCase.FullVariableDataContext.FullVariableDataContext.g.cs
// <auto-generated />

#nullable enable

using Microsoft.Data.SqlClient;

using SQuiL;

namespace TestCase;

partial class FullVariableDataContext : SQuiLBaseDataContext
{
	public async Task<FullVariableResponse> ProcessFullVariableAsync(
		FullVariableRequest request,
		CancellationToken cancellationToken = default!)
	{
		var builder = ConnectionStringBuilder("SQuiLDatabase");
		using SqlConnection connection = new(builder.ConnectionString);
		var command = connection.CreateCommand();
		
		List<SqlParameter> parameters = new()
		{
			new("EnvironmentName", System.Data.SqlDbType.VarChar, EnvironmentName.Length) { Value = EnvironmentName }, 
			new("Debug", System.Data.SqlDbType.Bit) { Value = EnvironmentName != "Production" }, 
			new("Scaler", System.Data.SqlDbType.BigInt) 
			{
				IsNullable = true,
				Value = request.Scaler ?? (object)System.DBNull.Value
			}
		};
		
		command.CommandText = Query(parameters);
		command.Parameters.AddRange(parameters);
		
		await connection.OpenAsync(cancellationToken);
		
		FullVariableResponse response = new();
		
		var isScaler = false;
		var isObject = false;
		var isTable = false;
		
		using var reader = await command.ExecuteReaderAsync(cancellationToken);
		
		do
		{
			var tableTag = reader.GetName(0);
			if(tableTag.StartsWith("__SQuiL__Table__Type__"))
			{
				switch (tableTag)
				{
					case "__SQuiL__Table__Type__Return_Scaler__":
					{
						if (isScaler) throw new Exception(
							"Already returned value for `Scaler`");
						
						isScaler = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						response.Scaler = !reader.IsDBNull(1) ? reader.GetInt32(1) : null;
						break;
					}
					case "__SQuiL__Table__Type__Return_Object__":
					{
						if (isObject) throw new Exception(
							"Already returned value for `Object`");
						
						isObject = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						if (response.Object is not null)
							throw new Exception("Object was already set.");
						
						if (reader.GetString(0) == "Return_Object")
						{
							response.Object = new(
								reader.GetInt32(reader.GetOrdinal("ObjectID")),
								reader.GetBoolean(reader.GetOrdinal("IsNeither")),
								reader.GetString(reader.GetOrdinal("PreferredName")));
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
					case "__SQuiL__Table__Type__Returns_Table__":
					{
						isTable = true;
						
						if (!await reader.ReadAsync(cancellationToken)) break;
						
						var indexTableID = reader.GetOrdinal("TableID");
						var indexIsBoth = reader.GetOrdinal("IsBoth");
						var indexNickName = reader.GetOrdinal("NickName");
						
						do
						{
							if (reader.GetString(0) == "Returns_Table")
							{
								response.Table.Add(new(
									reader.GetInt32(indexTableID),
									reader.GetBoolean(indexIsBoth),
									reader.GetString(indexNickName)));
							}
						}
						while (await reader.ReadAsync(cancellationToken));
						break;
					}
				}
			}
		}
		while (await reader.NextResultAsync(cancellationToken));
		
		if (!isScaler) throw new Exception("Expected return table `Scaler`)");
		if (!isObject) throw new Exception("Expected return table `Object`)");
		if (!isTable) throw new Exception("Expected return table `Table`)");
		
		return response;
		
		string inputObject(List<SqlParameter> parameters)
		{
			System.Text.StringBuilder query = new();
			query.Append("Insert Into @Object(ObjectID, IsMale, FirstName)");
			
			if (request.Object is null) return "";
			
			query.AppendLine();
			query.Append("Values (");
			
			AddParams(query, parameters, 0, "ParamObject", "ObjectID", System.Data.SqlDbType.BigInt, request.Object.ObjectID);
			query.Append(", ");
			AddParams(query, parameters, 0, "ParamObject", "IsMale", System.Data.SqlDbType.Bit, request.Object.IsMale);
			query.Append(", ");
			AddParams(query, parameters, 0, "ParamObject", "FirstName", System.Data.SqlDbType.VarChar, request.Object.FirstName, 100);
			
			query.Append(')');
			query.AppendLine(';');
			query.AppendLine();
			
			return query.ToString();
		}
		
		string inputTable(List<SqlParameter> parameters)
		{
			System.Text.StringBuilder query = new();
			query.Append("Insert Into @Table(TableID, IsFemale, LastName)");
			
			if (request.Table.Count == 0) return "";
			
			query.AppendLine(" Values");
			
			var comma = "";
			var index = 0;
			
			foreach(var item in request.Table)
			{
				index++;
				
				query.AppendLine(comma);
				query.Append('(');
				AddParams(query, parameters, index, "ParamsTable", "TableID", System.Data.SqlDbType.BigInt, item.TableID);
				query.Append(", ");
				AddParams(query, parameters, index, "ParamsTable", "IsFemale", System.Data.SqlDbType.Bit, item.IsFemale);
				query.Append(", ");
				AddParams(query, parameters, index, "ParamsTable", "LastName", System.Data.SqlDbType.VarChar, item.LastName, 100);
				query.Append(')');
				
				comma = ",";
			}
			
			query.AppendLine(';');
			query.AppendLine();
			
			return query.ToString();
		}
		
		string Query(List<SqlParameter> parameters) => $"""
		Declare @Param_Object table(
			[__SQuiL__Table__Type__Param_Object__] varchar(max) default('Param_Object'),
			ObjectID int,
			IsMale bit,
			FirstName varchar(100));
		{inputObject(parameters)}
		
		Declare @Params_Table table(
			[__SQuiL__Table__Type__Params_Table__] varchar(max) default('Params_Table'),
			TableID int,
			IsFemale bit,
			LastName varchar(100));
		{inputTable(parameters)}
		
		Declare @Return_Scaler int;
		
		Declare @Return_Object table(
			[__SQuiL__Table__Type__Return_Object__] varchar(max) default('Return_Object'),
			ObjectID int,
			IsNeither bit,
			PreferredName varchar(100));
		
		Declare @Returns_Table table(
			[__SQuiL__Table__Type__Returns_Table__] varchar(max) default('Returns_Table'),
			TableID int,
			IsBoth bit,
			NickName varchar(100));
		
		Use [{builder.InitialCatalog}];
		
		Select 1;
		
		Select 'Return_Scaler' As [__SQuiL__Table__Type__Return_Scaler__], @Return_Scaler
		
		""";
	}
}
