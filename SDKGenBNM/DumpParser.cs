using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppSDK
{
    public class DumpClass
    {
        public string? Name { get; set; }
        public string? Namespace { get; set; }
        public string? BaseType { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public List<DumpField> Fields { get; set; } = new List<DumpField>();
        public List<DumpMethod> Methods { get; set; } = new List<DumpMethod>();
        public bool IsEnum { get; set; }
        public bool IsStruct { get; set; }
        public bool IsSealed { get; set; }
        public bool IsAbstract { get; set; }
        public string? Module { get; set; }
    }

    public class DumpField
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public bool IsStatic { get; set; }
        public string? Offset { get; set; }
        public object? ConstantValue { get; set; }
        public bool IsLiteral { get; set; }
    }

    public class DumpMethod
    {
        public string? Name { get; set; }
        public string? ReturnType { get; set; }
        public List<string> Modifiers { get; set; } = new List<string>();
        public List<DumpParameter> Parameters { get; set; } = new List<DumpParameter>();
        public bool IsStatic { get; set; }
        public bool IsConstructor { get; set; }
        public string? Offset { get; set; }
    }

    public class DumpParameter
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool IsOut { get; set; }
    }

    public class DumpParser
    {
        private Dictionary<string, List<DumpClass>> namespaces = new Dictionary<string, List<DumpClass>>();
        private string currentNamespace = "";
        private DumpClass? currentClass = null;
        private string currentModule = "";
        private bool insideClass = false;
        private int braceLevel = 0;

        public Dictionary<string, List<DumpClass>> ParseDumpFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            var detectedNamespaces = new HashSet<string>();
            var detectedClasses = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("// Namespace: "))
                {
                    var ns = line.Substring("// Namespace: ".Length).Trim();
                    if (string.IsNullOrEmpty(ns)) ns = "GlobalNamespace";
                    detectedNamespaces.Add(ns);
                }
                if (line.Contains(" class ") || line.Contains(" struct ") || line.Contains("enum "))
                {
                    string? className = null;
                    if (line.Contains(" class "))
                    {
                        var parts = line.Split(new[] { " class " }, StringSplitOptions.None);
                        className = parts.Length > 1 ? parts[1].Trim() : null;
                        if (className != null && className.Contains(" : "))
                            className = className.Split(new[] { " : " }, StringSplitOptions.None)[0].Trim();
                        if (className != null)
                            className = className.Replace("{", "").Trim();
                    }
                    else if (line.Contains(" struct "))
                    {
                        var parts = line.Split(new[] { " struct " }, StringSplitOptions.None);
                        className = parts.Length > 1 ? parts[1].Trim() : null;
                        if (className != null)
                            className = className.Replace("{", "").Trim();
                    }
                    else if (line.Contains("enum "))
                    {
                        var match = Regex.Match(line, @"enum\s+(\w+)");
                        if (match.Success)
                            className = match.Groups[1].Value;
                    }
                    if (!string.IsNullOrEmpty(className))
                        detectedClasses.Add(className);
                }

                ParseLine(lines, i);
            }

            return namespaces;
        }

        private void ParseLine(string[] lines, int index)
        {
            var line = lines[index].Trim();

            if (line.StartsWith("// Dll : "))
            {
                currentModule = line.Substring("// Dll : ".Length).Trim();
                return;
            }

            if (line.StartsWith("// Namespace: "))
            {
                currentNamespace = line.Substring("// Namespace: ".Length).Trim();
                if (string.IsNullOrEmpty(currentNamespace))
                {
                    currentNamespace = "GlobalNamespace";
                }
                return;
            }

            if (line.Contains("{"))
                braceLevel++;
            if (line.Contains("}"))
            {
                braceLevel--;
                if (braceLevel <= 0 && insideClass)
                {
                    insideClass = false;
                    currentClass = null;
                }
            }

            if (!insideClass && (line.Contains(" class ") || line.Contains(" struct ") || line.Contains("enum ")))
            {
                ParseClassDeclaration(lines, index, line);
                return;
            }

            if (insideClass && currentClass != null)
            {
                if (line.Contains("; // 0x"))
                {
                    ParseField(line);
                }
                else if (line.Contains("() { }") || line.Contains(") { }"))
                {
                    ParseMethod(line);
                }
                else if (currentClass.IsEnum && line.Contains(" = "))
                {
                    ParseEnumField(line);
                }
            }
        }

        private void ParseClassDeclaration(string[] lines, int index, string line)
        {
            if (string.IsNullOrEmpty(currentNamespace) || currentNamespace == "GlobalNamespace")
            {
                for (int j = index - 1; j >= Math.Max(0, index - 12); j--)
                {
                    var prev = lines[j].Trim();
                    if (prev.StartsWith("// Namespace: "))
                    {
                        var ns = prev.Substring("// Namespace: ".Length).Trim();
                        if (!string.IsNullOrEmpty(ns))
                        {
                            currentNamespace = ns;
                        }
                        break;
                    }
                }
            }

            var classObj = new DumpClass();
            classObj.Namespace = currentNamespace;
            classObj.Module = currentModule;

            if (line.Contains(" class "))
            {
                var parts = line.Split(new[] { " class " }, StringSplitOptions.None);
                var modifierPart = parts[0].Trim();
                var namePart = parts[1].Trim();

                var modifiers = modifierPart.Split(' ').Where(m => !string.IsNullOrEmpty(m)).ToList();
                classObj.Modifiers = modifiers;
                classObj.IsSealed = modifiers.Contains("sealed");
                classObj.IsAbstract = modifiers.Contains("abstract");

                if (namePart.Contains(" : "))
                {
                    var nameAndBase = namePart.Split(new[] { " : " }, StringSplitOptions.None);
                    classObj.Name = nameAndBase[0].Trim();
                    var basePart = nameAndBase[1].Trim();
                    // strip any trailing inline comment like "// TypeDefIndex: N"
                    var commentIdx = basePart.IndexOf("//");
                    if (commentIdx >= 0)
                        basePart = basePart.Substring(0, commentIdx).Trim();
                    // extract the first base type while ignoring commas inside generic angle brackets
                    int depth = 0;
                    int cut = basePart.Length;
                    for (int k = 0; k < basePart.Length; k++)
                    {
                        var ch = basePart[k];
                        if (ch == '<') depth++;
                        else if (ch == '>') depth = Math.Max(0, depth - 1);
                        else if (ch == ',' && depth == 0)
                        {
                            cut = k;
                            break;
                        }
                    }
                    var firstBase = (cut <= basePart.Length) ? basePart.Substring(0, cut).Trim() : basePart.Trim();
                    classObj.BaseType = firstBase;
                }
                else
                {
                    classObj.Name = namePart.Replace("{", "").Trim();
                }
            }
            else if (line.Contains(" struct "))
            {
                var parts = line.Split(new[] { " struct " }, StringSplitOptions.None);
                var modifierPart = parts[0].Trim();
                var namePart = parts[1].Trim();

                classObj.Modifiers = modifierPart.Split(' ').Where(m => !string.IsNullOrEmpty(m)).ToList();
                if (namePart.Contains(" : "))
                {
                    var nameAndBase = namePart.Split(new[] { " : " }, StringSplitOptions.None);
                    var rawName = nameAndBase[0].Trim();
                    var commentIdx = rawName.IndexOf("//");
                    if (commentIdx >= 0)
                        rawName = rawName.Substring(0, commentIdx).Trim();
                    classObj.Name = rawName.Replace("{", "").Trim();
                }
                else
                {
                    classObj.Name = namePart.Replace("{", "").Trim();
                }

                classObj.IsStruct = true;
            }
            else if (line.Contains("enum "))
            {
                var match = Regex.Match(line, @"enum\s+(\w+)");
                if (match.Success)
                {
                    classObj.Name = match.Groups[1].Value;
                    classObj.IsEnum = true;
                }
            }

            classObj.Name = CleanName(classObj.Name);

            if (!namespaces.ContainsKey(currentNamespace))
            {
                namespaces[currentNamespace] = new List<DumpClass>();
            }
            namespaces[currentNamespace].Add(classObj);

            currentClass = classObj;
            insideClass = true;
        }

        private void ParseField(string line)
        {
            if (currentClass == null) return;

            try
            {
                var match = Regex.Match(line, @"^(.+?)\s+(\w+);\s*//\s*0x([A-Fa-f0-9]+)");
                if (match.Success)
                {
                    var typeAndModifiers = match.Groups[1].Value.Trim();
                    var fieldName = match.Groups[2].Value;
                    var offset = match.Groups[3].Value;

                    var field = new DumpField();
                    field.Name = fieldName;
                    field.Offset = offset;

                    var parts = typeAndModifiers.Split(' ').Where(p => !string.IsNullOrEmpty(p)).ToList();
                    var modifiers = new List<string>();
                    string fieldType = "";

                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] == "public" || parts[i] == "private" || parts[i] == "protected" ||
                            parts[i] == "static" || parts[i] == "readonly" || parts[i] == "const")
                        {
                            modifiers.Add(parts[i]);
                            if (parts[i] == "static")
                                field.IsStatic = true;
                        }
                        else
                        {
                            fieldType = string.Join(" ", parts.Skip(i));
                            break;
                        }
                    }

                    field.Type = fieldType;
                    field.Modifiers = modifiers;
                    currentClass.Fields.Add(field);
                }
            }
            catch (Exception)
            {
            }
        }

        private void ParseMethod(string line)
        {
            if (currentClass == null) return;

            try
            {
                var match = Regex.Match(line, @"^(.+?)\s+(\w+)\s*\(([^)]*)\)\s*\{\s*\}");
                if (match.Success)
                {
                    var returnTypeAndModifiers = match.Groups[1].Value.Trim();
                    var methodName = match.Groups[2].Value;
                    var parametersStr = match.Groups[3].Value.Trim();

                    var method = new DumpMethod();
                    method.Name = methodName;

                    var parts = returnTypeAndModifiers.Split(' ').Where(p => !string.IsNullOrEmpty(p)).ToList();
                    var modifiers = new List<string>();
                    string returnType = "";

                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] == "public" || parts[i] == "private" || parts[i] == "protected" ||
                            parts[i] == "static" || parts[i] == "virtual" || parts[i] == "override" ||
                            parts[i] == "abstract" || parts[i] == "sealed")
                        {
                            modifiers.Add(parts[i]);
                            if (parts[i] == "static")
                                method.IsStatic = true;
                        }
                        else
                        {
                            returnType = string.Join(" ", parts.Skip(i));
                            break;
                        }
                    }

                    method.ReturnType = returnType;
                    method.Modifiers = modifiers;
                    method.IsConstructor = methodName == ".ctor" || methodName == currentClass.Name;

                    if (!string.IsNullOrEmpty(parametersStr))
                    {
                        var paramParts = parametersStr.Split(',');
                        foreach (var paramPart in paramParts)
                        {
                            var paramTrimmed = paramPart.Trim();
                            if (!string.IsNullOrEmpty(paramTrimmed))
                            {
                                var paramTokens = paramTrimmed.Split(' ').Where(t => !string.IsNullOrEmpty(t)).ToArray();
                                if (paramTokens.Length >= 2)
                                {
                                    var param = new DumpParameter();
                                    param.Type = string.Join(" ", paramTokens.Take(paramTokens.Length - 1));
                                    param.Name = paramTokens.Last();
                                    param.IsOut = param.Type.Contains("out ");
                                    method.Parameters.Add(param);
                                }
                            }
                        }
                    }

                    currentClass.Methods.Add(method);
                }
            }
            catch (Exception)
            {
            }
        }

        private void ParseEnumField(string line)
        {
            if (currentClass == null || !currentClass.IsEnum) return;

            try
            {
                var match = Regex.Match(line, @"(\w+)\s*=\s*([^,]+)");
                if (match.Success)
                {
                    var fieldName = match.Groups[1].Value;
                    var value = match.Groups[2].Value.Replace(",", "").Trim();

                    var field = new DumpField();
                    field.Name = fieldName;
                    field.Type = "int";
                    field.IsLiteral = true;
                    field.IsStatic = true;

                    if (int.TryParse(value, out int intValue))
                    {
                        field.ConstantValue = intValue;
                    }
                    else
                    {
                        field.ConstantValue = value;
                    }

                    currentClass.Fields.Add(field);
                }
            }
            catch (Exception)
            {
            }
        }

        private string CleanName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";

            return name.Replace("<", "").Replace(">", "").Replace("`", "").Replace("{", "").Replace("}", "").Trim();
        }
    }
}
