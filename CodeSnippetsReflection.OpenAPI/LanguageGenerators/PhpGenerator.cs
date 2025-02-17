﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeSnippetsReflection.OpenAPI.ModelGraph;
using CodeSnippetsReflection.StringExtensions;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Services;

namespace CodeSnippetsReflection.OpenAPI.LanguageGenerators;

public class PhpGenerator : ILanguageGenerator<SnippetModel, OpenApiUrlTreeNode>
{
    private const string ClientVarName = "$graphServiceClient";
    private const string ClientVarType = "GraphServiceClient";
    private const string ScopesVarName = "$scopes";
    private const string TokenContextVarName = "$tokenRequestContext";
    private const string RequestBodyVarName = "requestBody";
    private const string QueryParametersVarName = "queryParameters";
    private const string RequestConfigurationVarName = "requestConfiguration";

    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract",
        "and",
        "array",
        "as",
        "break",
        "callable",
        "case",
        "catch",
        "class",
        "clone",
        "const",
        "continue",
        "declare",
        "default",
        "die",
        "do",
        "echo",
        "else",
        "elseif",
        "empty",
        "enddeclare",
        "endfor",
        "endforeach",
        "endif",
        "endswitch",
        "endwhile",
        "eval",
        "exit",
        "extends",
        "final",
        "finally",
        "fn",
        "for",
        "foreach",
        "function",
        "global",
        "goto",
        "if",
        "implements",
        "include",
        "include_once",
        "instanceof",
        "insteadof",
        "interface",
        "isset",
        "list",
        "match",
        "namespace",
        "new",
        "or",
        "print",
        "private",
        "protected",
        "public",
        "readonly",
        "require",
        "require_once",
        "return",
        "static",
        "switch",
        "throw",
        "trait",
        "try",
        "unset",
        "use",
        "var",
        "while",
        "xor",
        "yield",
        "yield from"
    };

    public string GenerateCodeSnippet(SnippetModel snippetModel)
    {
        var indentManager = new IndentManager();
        var codeGraph = new SnippetCodeGraph(snippetModel);
        var payloadSb = new StringBuilder(
            "<?php" + Environment.NewLine + Environment.NewLine +
            "// THIS SNIPPET IS A PREVIEW VERSION OF THE SDK. NON-PRODUCTION USE ONLY" + Environment.NewLine +
            $"{ClientVarName} = new {ClientVarType}({TokenContextVarName}, {ScopesVarName});{Environment.NewLine + Environment.NewLine}");
        if (codeGraph.HasBody())
        {
            WriteObjectProperty(RequestBodyVarName, payloadSb, codeGraph.Body, indentManager);
        }
        WriteRequestExecutionPath(codeGraph, payloadSb, indentManager);
        return payloadSb.ToString().Trim();
    }

    private static void WriteRequestExecutionPath(SnippetCodeGraph codeGraph, StringBuilder payloadSb, IndentManager indentManager)
    {
        var method = codeGraph.HttpMethod.Method.ToLower();
        var configParameter = codeGraph.HasHeaders() || codeGraph.HasParameters()
            ? $"{RequestConfigurationVarName}"
            : string.Empty;
        var bodyParameter = codeGraph.HasBody()
            ? $"{RequestBodyVarName}"
            : string.Empty;
        var optionsParameter = codeGraph.HasOptions() ? "options" : string.Empty;
        var returnVar = codeGraph.HasReturnedBody() ? "$result = " : string.Empty;
        var parameterList = GetActionParametersList(bodyParameter, configParameter, optionsParameter);
        payloadSb.AppendLine(GetRequestConfiguration(codeGraph, indentManager));
        payloadSb.AppendLine($"{returnVar}{ClientVarName}->{GetFluentApiPath(codeGraph.Nodes, codeGraph)}->{method}({parameterList})->wait();");
    }
    private static string GetRequestQueryParameters(SnippetCodeGraph model, string configClassName) 
    {
        var payloadSb = new StringBuilder();
        if (!model.HasParameters()) return default;
        
        payloadSb.AppendLine($"${QueryParametersVarName} = {configClassName}::createQueryParameters();");
        var requestQueryParameters = model.Parameters.ToDictionary(x => x.Name);
        
        foreach(var (key, value) in requestQueryParameters)
        {
            payloadSb.AppendLine(
                $"${QueryParametersVarName}->{NormalizeVariableName(key)} = {EvaluateParameter(value)};");
        }

        payloadSb.AppendLine($"${RequestConfigurationVarName}->queryParameters = ${QueryParametersVarName};");
        return payloadSb.ToString();

    }
    
    private static string EvaluateParameter(CodeProperty param)
    {
        return param.PropertyType switch
        {
            PropertyType.Array =>
                $"[{string.Join(",", param.Children.Select(static x => $"\"{x.Value}\"").ToList())}]",
            PropertyType.Boolean or PropertyType.Int32 or PropertyType.Double or PropertyType.Float32 or PropertyType.Float64 or PropertyType.Int64 => param.Value,
            _ => $"\"{param.Value.EscapeQuotes()}\""
        };
    }
    private static string GetRequestConfiguration(SnippetCodeGraph codeGraph, IndentManager indentManager)
    {
        var payloadSb = new StringBuilder();

        if (codeGraph.HasParameters() || codeGraph.HasHeaders() || codeGraph.HasOptions())
        {
            var className = $"{codeGraph.Nodes.Last().GetClassName("RequestBuilder").ToFirstCharacterUpperCase()}{codeGraph.HttpMethod.Method.ToLowerInvariant().ToFirstCharacterUpperCase()}RequestConfiguration";
            payloadSb.AppendLine($"${RequestConfigurationVarName} = new {className}();");
            var requestHeadersPayload = GetRequestHeaders(codeGraph, indentManager);
            var queryParamsPayload = GetRequestQueryParameters(codeGraph, className);
            if (!string.IsNullOrEmpty(requestHeadersPayload))
                payloadSb.AppendLine($"{requestHeadersPayload}");
            if (!string.IsNullOrEmpty(queryParamsPayload))
                payloadSb.AppendLine($"{queryParamsPayload}");
        }
        
        return payloadSb.Length > 0 ? payloadSb.ToString() : default;
    }
    private static string GetActionParametersList(params string[] parameters) {
        var nonEmptyParameters = parameters.Where(static p => !string.IsNullOrEmpty(p));
        var emptyParameters = nonEmptyParameters.ToList();
        if(emptyParameters.Any())
            return string.Join(", ", emptyParameters.Select(static x => $"${x}").Aggregate(static (a, b) => $"{a}, {b}"));
        return string.Empty;
    }
    
    private static string GetRequestHeaders(SnippetCodeGraph snippetModel, IndentManager indentManager) {
        var payloadSb = new StringBuilder();
        var filteredHeaders = snippetModel.Headers?.Where(static h => !h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if(filteredHeaders != null && filteredHeaders.Any()) {
            payloadSb.AppendLine("$headers = [");
            indentManager.Indent(2);
            filteredHeaders.ForEach(h =>
                payloadSb.AppendLine($"{indentManager.GetIndent()}'{h.Name}' => '{h.Value.Replace("\'", "\\'")}',")
            );
            indentManager.Unindent();
            payloadSb.AppendLine($"{indentManager.GetIndent()}];");
            payloadSb.AppendLine($"${RequestConfigurationVarName}->headers = $headers;");
            return payloadSb.ToString();
        }
        return default;
    }
    private static string NormalizeQueryParameterName(string queryParam) => queryParam?.TrimStart('$').ToFirstCharacterLowerCase();
    private static void WriteObjectProperty(string propertyAssignment, StringBuilder payloadSb, CodeProperty codeProperty, IndentManager indentManager, string childPropertyName = default)
    {
        var childPosition = 0;
        var objectType = (codeProperty.TypeDefinition ?? codeProperty.Name).ToFirstCharacterUpperCase();
        payloadSb.AppendLine($"${(childPropertyName ?? propertyAssignment).ToFirstCharacterLowerCase()} = new {ReplaceReservedWord(objectType)}();");
        foreach(var child in codeProperty.Children)
        {
            var newChildName = (childPropertyName ?? "") + child.Name.ToFirstCharacterUpperCase();
            WriteCodeProperty(childPropertyName ?? propertyAssignment, payloadSb, codeProperty, child, indentManager, ++childPosition, newChildName);
        }
    }
    private static void WriteCodeProperty(string propertyAssignment, StringBuilder payloadSb, CodeProperty parent, CodeProperty child, IndentManager indentManager, int childPosition = 0, string childPropertyName = default, bool fromMap = false)
    {
        var isArray = parent.PropertyType == PropertyType.Array;
        var isMap = parent.PropertyType == PropertyType.Map;
        var fromArray = parent.PropertyType == PropertyType.Array;

        var propertyName = NormalizeQueryParameterName(child.Name.ToFirstCharacterLowerCase());
        switch (child.PropertyType) {
			case PropertyType.String:
            case PropertyType.Guid:
                WriteStringProperty(propertyAssignment, parent, payloadSb, indentManager, child);
                break;
			case PropertyType.Int32:
            case PropertyType.Int64:
            case PropertyType.Double:
            case PropertyType.Float32:
            case PropertyType.Float64:
                if (!isMap && !isArray)
                    payloadSb.AppendLine(
                        $"${propertyAssignment.ToFirstCharacterLowerCase()}->set{EscapePropertyNameForSetterAndGetter(propertyName.ToFirstCharacterUpperCase())}({child.Value});");
                else
                    payloadSb.Append($"{child.Value},");
                break;
			case PropertyType.Boolean:
                if (!isMap && !isArray) {
                    payloadSb.AppendLine(
                        $"${propertyAssignment.ToFirstCharacterLowerCase()}->set{EscapePropertyNameForSetterAndGetter(propertyName.ToFirstCharacterUpperCase())}({child.Value.ToLower()});");
                }
                else
                {
                    payloadSb.Append($"{child.Value.ToLower()},");
                }
                break;
			case PropertyType.Null:
                WriteNullProperty(propertyAssignment, parent, payloadSb, indentManager, child);
                break;
            case PropertyType.Object: 
                WriteObjectProperty(propertyAssignment.ToFirstCharacterLowerCase(), payloadSb, child, indentManager, childPropertyName);
                if (!fromArray)
                    payloadSb.AppendLine(
                        $"${propertyAssignment.ToFirstCharacterLowerCase()}->set{EscapePropertyNameForSetterAndGetter(child.Name.ToFirstCharacterUpperCase())}(${(childPropertyName ?? propertyName).ToFirstCharacterLowerCase()});");
                break;
			case PropertyType.Array:
				WriteArrayProperty(propertyAssignment.ToFirstCharacterLowerCase(), child.Name, payloadSb, parent, child, indentManager, fromMap: fromMap); 
                break;
            case PropertyType.Enum:
                WriteEnumValue(payloadSb, propertyAssignment.ToFirstCharacterLowerCase(), child, parent);
                break;
            case PropertyType.Base64Url:
                WriteBase64Url(propertyAssignment, parent, payloadSb, indentManager, child);
                break;
            case PropertyType.Map:
                WriteMapValue(payloadSb, propertyAssignment.ToFirstCharacterLowerCase(), parent, child, indentManager, fromMap: fromMap);
                break;
            case PropertyType.DateTime:
                WriteDateTimeValue(payloadSb, propertyAssignment.ToFirstCharacterLowerCase(), parent, child, indentManager);
                break;
            case PropertyType.TimeOnly:
            case PropertyType.DateOnly:
                WriteDateOnlyTimeOnly(payloadSb, propertyAssignment.ToFirstCharacterLowerCase(), parent, child, indentManager, child.PropertyType == PropertyType.TimeOnly ? "Time" : "Date");
                break;
            case PropertyType.Duration:
                WriteDurationValue(payloadSb, propertyAssignment.ToFirstCharacterLowerCase(), parent, child,
                    indentManager);
                break;
			default:
				throw new NotImplementedException($"Unsupported PropertyType: {child.PropertyType.GetDisplayName()}");
        }
	}
    
    private static void WriteDurationValue(StringBuilder payloadSb, string propertyAssignment, CodeProperty parent,
        CodeProperty child, IndentManager indentManager) {
        var fromObject = parent.PropertyType == PropertyType.Object;
        var assignmentValue = $"new \\DateInterval(\'{child.Value}\')";
        if (fromObject)
            payloadSb.AppendLine(
                $"{indentManager.GetIndent()}${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(child.Name)}({assignmentValue});");
        else
            payloadSb.Append($"{indentManager.GetIndent()}{assignmentValue},");
    } 
    private static void WriteDateOnlyTimeOnly(StringBuilder payloadSb, string propertyAssignment, CodeProperty parent,
        CodeProperty child, IndentManager indentManager, string typeName) {
        var fromObject = parent.PropertyType == PropertyType.Object;
        var assignmentValue = $"new {typeName}(\'{child.Value}\')";
        if (fromObject)
            payloadSb.AppendLine(
                $"{indentManager.GetIndent()}${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(child.Name)}({assignmentValue});");
        else
            payloadSb.Append($"{indentManager.GetIndent()}{assignmentValue},");
    }
    private static void WriteDateTimeValue(StringBuilder payloadSb, string propertyAssignment, CodeProperty parent,
        CodeProperty child, IndentManager indentManager)
    {
        var fromObject = parent.PropertyType == PropertyType.Object;
        var assignmentValue = $"new \\DateTime(\'{child.Value}\')";
        if (fromObject)
            payloadSb.AppendLine(
                $"{indentManager.GetIndent()}${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(child.Name)}({assignmentValue});");
        else
            payloadSb.Append($"{indentManager.GetIndent()}{assignmentValue},");
    }
    private static void WriteNullProperty(string propertyAssignment, CodeProperty parent, StringBuilder payloadSb, IndentManager indentManager, CodeProperty child)
    {
        var fromObject = parent.PropertyType == PropertyType.Object;
        if (fromObject)
            payloadSb.AppendLine(
                $"${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(child.Name)}(null);");
        else
            payloadSb.Append($"{indentManager.GetIndent()}null,");
    }

    private static void WriteMapValue(StringBuilder payloadSb, string propertyAssignment, CodeProperty parent, CodeProperty currentProperty, IndentManager indentManager, bool fromMap = false)
    {
        if (parent.PropertyType == PropertyType.Object)
        {
            payloadSb.AppendLine($"${currentProperty.Name} = [");
        }
        if (fromMap)
        {
            if (parent.PropertyType == PropertyType.Array) indentManager.Indent();
            payloadSb.AppendLine($"{indentManager.GetIndent()}[");
        }

        var childPosition = 0;
        indentManager.Indent();
        foreach (var child in currentProperty.Children)
        {
            var payLoad = new StringBuilder();
            payloadSb.Append($"{indentManager.GetIndent()}\'{child.Name}\' => ");
            var p2 = child;
            if (p2.PropertyType == PropertyType.Object)
            {
                p2.PropertyType = PropertyType.Map;
            }
            WriteCodeProperty(propertyAssignment, payLoad, currentProperty, p2, indentManager, ++childPosition, default, true);
            payLoad.AppendLine();
            payloadSb.AppendLine(payLoad.ToString().Trim());
        }
        indentManager.Unindent();
        payloadSb.AppendLine($"{indentManager.GetIndent()}{(parent.PropertyType == PropertyType.Object ? "];" : "]," )}");
        var fromObject = false;
        switch (parent.PropertyType)
        {
            case PropertyType.Object:
                fromObject = true;
                payloadSb.AppendLine(
                    $"${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(currentProperty.Name)}(${EscapePropertyNameForSetterAndGetter(currentProperty.Name).ToFirstCharacterLowerCase()});");
                break;
            case PropertyType.Array:
                indentManager.Unindent();
                break;
        }

        if(!fromMap && !fromObject) payloadSb.AppendLine();
    }

    private static void WriteArrayProperty(string propertyAssignment, string objectName, StringBuilder payloadSb,
        CodeProperty parentProperty, CodeProperty codeProperty, IndentManager indentManager, bool fromMap = false)
    {
        var hasSchema = codeProperty.PropertyType == PropertyType.Object;
        var arrayName = $"{objectName.ToFirstCharacterLowerCase()}Array";
        var builder = new StringBuilder();
        if (hasSchema) 
            builder.AppendLine($"${arrayName} = [];");
        else if (fromMap)
        {
            builder.AppendLine($"{indentManager.GetIndent()}[");
            indentManager.Indent();
        }
        else if (codeProperty.Children.FirstOrDefault().PropertyType != PropertyType.Object)
        {
            builder.Append($"{indentManager.GetIndent()}[");
            indentManager.Indent();
        }

        var childPosition = 0;
        CodeProperty lastProperty = default;
        foreach (var property in codeProperty.Children)
        {
            var childPropertyName = $"{EscapePropertyNameForSetterAndGetter(codeProperty.Name)}{EscapePropertyNameForSetterAndGetter(property.Name)}{++childPosition}".ToFirstCharacterLowerCase();
            var propertyCopy = property;
            if (fromMap && property.PropertyType == PropertyType.Object) propertyCopy.PropertyType = PropertyType.Map;

            WriteCodeProperty(propertyAssignment, builder, codeProperty, propertyCopy, indentManager, childPosition,
                    childPropertyName, fromMap: fromMap);

            if (property.PropertyType == PropertyType.Object && codeProperty.PropertyType == PropertyType.Array && !fromMap)
            {
                builder.AppendLine($"${arrayName} []= ${childPropertyName};");
            }

            lastProperty = property;
        }

        if (lastProperty.PropertyType == PropertyType.Object && !fromMap)
        {
            builder.AppendLine(
                $"${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(codeProperty.Name)}(${arrayName});");
            payloadSb.AppendLine(builder.ToString());
        }
        else if (lastProperty.PropertyType != PropertyType.Object && lastProperty.PropertyType != PropertyType.Map && parentProperty.PropertyType != PropertyType.Object)
        {
            payloadSb.Append(builder.ToString().Trim(',', '\n')).Append("],");
        } else 
        {
            builder.Append($"{indentManager.GetIndent()}]");
            if (parentProperty.PropertyType == PropertyType.Object)
                payloadSb.AppendLine(
                    $"${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(codeProperty.Name)}({builder.ToString().Trim()});");
            else
                payloadSb.Append($"{builder.ToString().Trim()},");
        }

        indentManager.Unindent();
    }
    private static string ReplaceReservedWord(string type) =>
        ReservedTypeNames.Contains(type) ? $"Escaped{type.ToFirstCharacterUpperCase()}" : type;

    private static void WriteEnumValue(StringBuilder payloadSb,string parentPropertyName, CodeProperty currentProperty, CodeProperty parent)
    {
        var enumParts = currentProperty.Value.Split('.');
        var enumClass = enumParts.First();
        var enumValue = enumParts.Last().ToFirstCharacterLowerCase();
        var fromObject = parent.PropertyType == PropertyType.Object;
        var value = $"new {ReplaceReservedWord(enumClass)}('{enumValue}')";
        if (fromObject)
            payloadSb.AppendLine(
                $"${parentPropertyName}->set{EscapePropertyNameForSetterAndGetter(currentProperty.Name)}({value});");
        else
            payloadSb.Append($"{value},");
    }

    private static void WriteBase64Url(string propertyAssignment, CodeProperty parent, StringBuilder payloadSb, IndentManager indentManager, CodeProperty child)
    {
        var fromObject = parent.PropertyType == PropertyType.Object;
        var value = $"base64_decode(\'{child.Value}\')";
        if (fromObject)
            payloadSb.AppendLine(
                $"{indentManager.GetIndent()}${propertyAssignment}->set{EscapePropertyNameForSetterAndGetter(child.Name)}({value});");
        else
            payloadSb.Append($"{value},");
    }

    private static void WriteStringProperty(string propertyAssignment, CodeProperty parent, StringBuilder payloadSb, IndentManager indentManager, CodeProperty codeProperty)
    {
        var fromObject = parent.PropertyType == PropertyType.Object;

        if (fromObject)
        {
            payloadSb.AppendLine(
                $"${indentManager.GetIndent()}{propertyAssignment.ToFirstCharacterLowerCase()}->set{EscapePropertyNameForSetterAndGetter(codeProperty.Name)}('{codeProperty.Value.EscapeQuotesInLiteral("\"", "\\'")}');");
        }
        else
            payloadSb.Append($"\'{codeProperty.Value.EscapeQuotesInLiteral("\"", "\\'")}\', ");
    }

    private static string NormalizeVariableName(string variable) =>
        variable.Replace(".", string.Empty).Replace("-", string.Empty);

    private static string EscapePropertyNameForSetterAndGetter(string propertyName)
    {
        return propertyName?.Split('.', '@', '-').Select(x => x.ToFirstCharacterUpperCase()).Aggregate((a, b) => a + b);
    }
    private static string GetFluentApiPath(IEnumerable<OpenApiUrlTreeNode> nodes, SnippetCodeGraph codeGraph)
    {
        var openApiUrlTreeNodes = nodes.ToList();
        if (!(openApiUrlTreeNodes?.Any() ?? false)) return string.Empty;
        var result = openApiUrlTreeNodes.Select(x =>
            {
                if (x.Segment.IsCollectionIndex())
                    return $"ById{x.Segment.Replace("{", "('").Replace("}", "')")}"; 
                if (x.Segment.IsFunctionWithParameters())
                {
                    var functionName = x.Segment.Split('(').First();
                    functionName = functionName.Split(".",StringSplitOptions.RemoveEmptyEntries)
                                                .Select(static s => s.ToFirstCharacterUpperCase())
                                                .Aggregate(static (a, b) => $"{a}{b}");
                    var parameters = codeGraph.PathParameters
                        .Select(static s => $"With{s.Name.ToFirstCharacterUpperCase()}")
                        .Aggregate(static (a, b) => $"{a}{b}");

                    // use the existing WriteObjectFromCodeProperty functionality to write the parameters as if they were a comma seperated array so as to automatically infer type handling from the codeDom :)
                    var parametersBuilder = new StringBuilder();
                    foreach (var codeProperty in codeGraph.PathParameters.OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var parameter = new StringBuilder();
                        WriteCodeProperty(string.Empty, parametersBuilder,new CodeProperty{PropertyType = PropertyType.Array}, codeProperty, new IndentManager(), 0, codeProperty.Name);
                        parametersBuilder.Append(parameter.ToString().Trim());//Do this to trim the surrounding whitespace generated
                    }
                    
                    return functionName.ToFirstCharacterLowerCase()
                           + parameters
                           + $"({parametersBuilder.ToString().TrimEnd(',')})" ;
                }
                if (x.Segment.IsFunction())
                    return x.Segment.Split('.')
                        .Select(static s => s.ToFirstCharacterUpperCase())
                        .Aggregate(static (a, b) => $"{a}{b}").ToFirstCharacterLowerCase() + "()";
                return x.Segment.ToFirstCharacterLowerCase() + "()";
            }).Aggregate(new List<string>(), (current, next) =>
            {
                if (next.StartsWith("ById"))
                {
                    var inBrackets = next[4..];
                    var indexerName = next[6..].Split('-')[0];
                    next = $"by{indexerName.ToFirstCharacterUpperCase()}Id{inBrackets}";
                }
                current.Add(next);
                return current;
            }).Aggregate(static (x, y) => $"{x.Trim('$')}->{y.Trim('$')}")
            .Replace("()()->", "()->");

        return result.EndsWith("()()") ? result[..^2] : result;
    }
}
