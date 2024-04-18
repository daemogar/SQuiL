﻿//HintName: SQuiLBaseDataContext.g.cs
// <auto-generated />

#nullable enable

namespace SQuiL;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using System.Collections.Generic;
using System;

public abstract partial class SQuiLBaseDataContext(IConfiguration Configuration)
{
	//public virtual string SettingName { get; } = "SQuiLDatabase";

	protected string EnvironmentName { get; } = Configuration.GetSection("EnvironmentName")?.Value
		?? Environment.GetEnvironmentVariable(Configuration.GetSection("EnvironmentVariable")?.Value ?? "ASPNETCORE_ENVIRONMENT")
		?? "Development";

	protected SqlConnectionStringBuilder ConnectionStringBuilder(string settingName)
	{
		return new SqlConnectionStringBuilder(Configuration.GetConnectionString(settingName)
			?? throw new Exception($"Cannot find a connection string in the appsettings for {settingName}."));
	}

	protected void AddParams(System.Text.StringBuilder query, List<SqlParameter> parameters, int index, string table, string name, System.Data.SqlDbType type, object value, int size = 0)
	{
		var parameter = $"@{table}_{index}_{name}";
		query.Append(parameter);

		if (size == 0)
		{
			parameters.Add(new(parameter, type) { Value = value });
			return;
		}

		parameters.Add(new(parameter, type, size) {
			Value = value is null || ((string)value).Length <= size
				? (value ?? "Null")
				: throw new Exception($"""
					ParamsTable model table property at index [{index}] has a string property [{name}]
					with more than {size} characters.
					""")
		});
	}
}