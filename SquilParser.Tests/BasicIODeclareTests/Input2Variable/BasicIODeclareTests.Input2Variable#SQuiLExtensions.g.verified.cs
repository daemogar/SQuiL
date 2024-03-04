﻿//HintName: SQuiLExtensions.g.cs
// <auto-generated />

#nullable enable

namespace Microsoft.Extensions.DependencyInjection;

public static class SQuiLExtensions
{
    public static bool IsLoaded => true;
    
    public static IServiceCollection AddSQuiLParser(
        this IServiceCollection services)
    {
        services.AddSingleton<TestCase.Input2VariableDataContext>();
        
        return services;
    }
}
