using System.Text;
using System.Collections.Generic;
using dnlib;
using dnlib.DotNet;
using System.Text.RegularExpressions;

public static class Utils
{
    public static bool StartsWithNumber(string str)
    {
        return char.IsDigit(str[0]);
    }

    public static string Il2CppTypeToCppType(TypeSig type)
    {
        if (type.IsGenericInstanceType)
        {
            return FormatIl2CppGeneric(type);
        }

        string result = type.FullName switch
        {
            "System.Int8" => "int8_t",
            "System.UInt8" => "uint8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Boolean" => "bool",
            "System.Decimal" => "BNM::Types::decimal",
            "System.Char" => "char",
            "System.Byte" => "BNM::Types::byte",
            "System.SByte" => "BNM::Types::sbyte",
            "System.String" => "BNM::Structures::Mono::String*",
            "UnityEngine.Vector2" => "BNM::Structures::Unity::Vector2",
            "UnityEngine.Vector3" => "BNM::Structures::Unity::Vector3",
            "UnityEngine.Quaternion" => "BNM::Structures::Unity::Quaternion",
            "UnityEngine.Rect" => "BNM::Structures::Unity::Rect",
            "System.Void" => "void",
            _ => "void*"
        };

        if (type.FullName.Contains("[]"))
            result = "BNM::Structures::Mono::Array<" + result + ">*";

        return result;
    }

    public static string FormatIl2CppGeneric(TypeSig type)
    {
        string result = "";

        if (type.GetName().StartsWith("List"))
        {
            result = "BNM::Structures::Mono::List<";
        }
        else if (type.GetName().StartsWith("Dictionary"))
        {
            result = "BNM::Structures::Mono::Dictionary<";
        }
        else
        {
            return "void*";
        }
        List<string> args = new List<string>();
        foreach (var arg in type.ToGenericInstSig().GenericArguments)
        {
            if (arg.IsGenericInstanceType)
            {
                args.Add(FormatIl2CppGeneric(arg));
            }
            else args.Add(Il2CppTypeToCppType(arg));
        }
        result += string.Join(", ", args.ToArray());
        result += ">*";
        return result;
    }

    public static string FormatInvalidName(string className)
    {
        string str = className.Trim()
            .Replace("<", "$")
            .Replace(">", "$")
            .Replace("|", "$")
            .Replace("-", "$")
            .Replace("`", "$")
            .Replace("=", "$")
            .Trim();
        if (string.IsNullOrEmpty(str))
            return "_";

        if (StartsWithNumber(str))
            str = "_" + str;

        return str;
    }

}