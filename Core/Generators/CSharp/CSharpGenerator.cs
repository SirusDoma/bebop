﻿using System;
using System.Linq;
using Core.Meta;
using Core.Meta.Extensions;
using Core.Meta.Interfaces;

namespace Core.Generators.CSharp
{
    
    public class CSharpGenerator : Generator
    {
        const int indentStep = 2;
        private static readonly string GeneratedAttribute = $"[System.CodeDom.Compiler.GeneratedCode(\"{ReservedWords.CompilerName}\", \"{ReservedWords.CompilerVersion}\")]";
        private static readonly string RecordAttribute = $"[BebopRecord]";
        private static readonly string EditorBrowsableAttribute = "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]";
        private const string HotPath = "[System.Runtime.CompilerServices.MethodImpl(BebopConstants.HotPath)]";

        private static readonly string WarningBlock = $@"//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:{ReservedWords.CompilerVersion}
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------";


        public CSharpGenerator(ISchema schema) : base(schema) { }

        private string FormatDocumentation(string documentation, int spaces)
        {
            var builder = new IndentedStringBuilder(spaces);
            builder.AppendLine("/// <summary>");
            foreach (var line in documentation.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                builder.AppendLine($"/// {line}");
            }
            builder.AppendLine("/// </summary>");
            return builder.ToString();
        }

        public override string Compile()
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine(WarningBlock);
            builder.AppendLine("using global::Bebop.Attributes;");
            builder.AppendLine("using global::Bebop.Runtime;");
            builder.AppendLine("//");
            builder.AppendLine($"// This source code was auto-generated by {ReservedWords.CompilerName}, Version={ReservedWords.CompilerVersion}.");
            builder.AppendLine("//");

            if (!string.IsNullOrWhiteSpace(Schema.Namespace))
            {
                builder.AppendLine($"namespace {Schema.Namespace.ToPascalCase()} {{");
                builder.Indent(indentStep);
            }
            foreach (var definition in Schema.Definitions.Values)
            {
                var definitionName = definition.Name.ToPascalCase();
                if (!string.IsNullOrWhiteSpace(definition.Documentation))
                {
                    builder.AppendLine(FormatDocumentation(definition.Documentation, 2));
                }
                builder.AppendLine(GeneratedAttribute);
                builder.AppendLine(RecordAttribute);
                if (definition.IsEnum())
                {
                    builder.AppendLine($"public enum {definition.Name} : uint {{");
                    builder.Indent(indentStep);
                    for (var i = 0; i < definition.Fields.Count; i++)
                    {
                        var field = definition.Fields.ElementAt(i);
                        if (!string.IsNullOrWhiteSpace(field.Documentation))
                        {
                            builder.AppendLine(FormatDocumentation(field.Documentation, 6));
                        }
                        builder.AppendLine($"{field.Name} = {field.ConstantValue}{(i + 1 < definition.Fields.Count ? "," : "")}");
                    }
                    builder.Dedent(indentStep);
                    builder.AppendLine("}");
                }
                else if (definition.IsMessage() || definition.IsStruct())
                {
                    var baseName = "Base" + definitionName;
                    builder.AppendLine($"public abstract class {baseName} {{");
                    builder.Indent(indentStep);
                    if (definition.OpcodeAttribute is not null)
                    {
                        builder.AppendLine($"public const uint OpCode = {definition.OpcodeAttribute.Value};");
                    }
                    if (definition.IsMessage())
                    {
                        builder.AppendLine("#nullable enable");
                    }
                    for (var i = 0; i < definition.Fields.Count; i++)
                    {
                        var field = definition.Fields.ElementAt(i);

                        if (!string.IsNullOrWhiteSpace(field.Documentation))
                        {
                            builder.AppendLine(FormatDocumentation(field.Documentation, 4));
                        }
                        if (field.DeprecatedAttribute is not null &&
                            !string.IsNullOrWhiteSpace(field.DeprecatedAttribute.Value))
                        {
                            builder.AppendLine($"[System.Obsolete(\"{field.DeprecatedAttribute.Value}\")]");
                        }
                        var type = TypeName(field.Type);
                        var opt = definition.Kind == AggregateKind.Message ? "?" : "";
                        var setOrInit = definition.IsReadOnly ? "init" : "set";
                        builder.AppendLine($"public {type}{opt} {field.Name.ToPascalCase()} {{ get; {setOrInit}; }}");
                    }
                    if (definition.IsMessage())
                    {
                        builder.AppendLine("#nullable disable");
                    }


                    builder.Dedent(indentStep);
                    builder.AppendLine("}");
                    builder.AppendLine("");
                    builder.AppendLine("/// <inheritdoc />");
                    builder.AppendLine(GeneratedAttribute);
                    builder.AppendLine(RecordAttribute);
                    builder.AppendLine($"public sealed class {definitionName} : {baseName} {{");
                    builder.Indent(indentStep);
                    builder.AppendLine(CompileEncodeHelper(definition));
                    builder.AppendLine(HotPath);
                    builder.AppendLine(GeneratedAttribute);
                    builder.AppendLine(EditorBrowsableAttribute);
                    builder.AppendLine($"internal static void EncodeInto({baseName} record, ref BebopWriter writer) {{");
                    builder.Indent(indentStep);
                    builder.AppendLine(CompileEncode(definition));
                    builder.Dedent(indentStep);
                    builder.AppendLine("}");
                    builder.AppendLine("");
                    builder.AppendLine(CompileDecodeHelper(definition, "byte[]"));
                    builder.AppendLine(CompileDecodeHelper(definition, "System.ReadOnlySpan<byte>"));
                    builder.AppendLine(CompileDecodeHelper(definition, "System.ReadOnlyMemory<byte>"));
                    builder.AppendLine(CompileDecodeHelper(definition, "System.ArraySegment<byte>"));

                    builder.AppendLine(HotPath);
                    builder.AppendLine(GeneratedAttribute);
                    builder.AppendLine(EditorBrowsableAttribute);
                    builder.AppendLine($"internal static {definitionName} DecodeFrom(ref BebopReader reader) {{");
                    builder.Indent(indentStep);
                    // when you do new T() the compile uses System.Activator::CreateInstance
                    // this non-generic variant avoids that penalty hit.
                    // https://devblogs.microsoft.com/premier-developer/dissecting-the-new-constraint-in-c-a-perfect-example-of-a-leaky-abstraction/
                    builder.AppendLine(CompileDecode(definition, false));
                    builder.Dedent(indentStep);
                    builder.AppendLine("}");

                    builder.AppendLine(HotPath);
                    builder.AppendLine(GeneratedAttribute);
                    builder.AppendLine(EditorBrowsableAttribute);
                    builder.AppendLine($"internal static T DecodeFrom<T>(ref BebopReader reader) where T: {baseName}, new() {{");
                    builder.Indent(indentStep);
                    // a generic decode method that allows for run-time polymorphism 
                    // this will initiate objects via a slower .ctor reflection
                    // the last 16 objects are cached.
                    builder.AppendLine(CompileDecode(definition, true));
                    builder.Dedent(indentStep);
                    builder.AppendLine("}");

                    builder.Dedent(indentStep);
                    builder.AppendLine("}");
                }
            }

            if (!string.IsNullOrWhiteSpace(Schema.Namespace))
            {
                builder.Dedent(indentStep);
                builder.AppendLine("}");
            }
            return builder.ToString();
        }

        public override void WriteAuxiliaryFiles(string outputPath)
        {

        }

        /// <summary>
        ///     Generate a C# type name for the given <see cref="TypeBase"/>.
        /// </summary>
        /// <param name="type">The field type to generate code for.</param>
        /// <param name="arraySizeVar">A variable name that will be formatted into the array initializer</param>
        /// <returns>The C# type name.</returns>
        private string TypeName(in TypeBase type, string arraySizeVar = "")
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "bool",
                        BaseType.Byte => "byte",
                        BaseType.UInt32 => "uint",
                        BaseType.Int32 => "int",
                        BaseType.Float32 => "float",
                        BaseType.Float64 => "double",
                        BaseType.String => "string",
                        BaseType.Guid => "System.Guid",
                        BaseType.UInt16 => "ushort",
                        BaseType.Int16 => "short",
                        BaseType.UInt64 => "ulong",
                        BaseType.Int64 => "long",
                        BaseType.Date => "System.DateTime",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                case ArrayType at:
                    return $"{(at.MemberType is ArrayType ? ($"{TypeName(at.MemberType, arraySizeVar)}[]") : $"{TypeName(at.MemberType)}[{arraySizeVar}]")}";
                case MapType mt:
                    return $"System.Collections.Generic.Dictionary<{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}>";
                case DefinedType dt:
                    var isEnum = Schema.Definitions[dt.Name].Kind == AggregateKind.Enum;
                    return $"{(isEnum ? string.Empty : "Base")}{dt.Name}";
            }
            throw new InvalidOperationException($"GetTypeName: {type}");
        }

        /// <summary>
        ///     Generate the body of the <c>DecodeFrom</c> function for the given <see cref="IDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated C# <c>DecodeFrom</c> function body.</returns>
        public string CompileDecode(IDefinition definition, bool useGenerics)
        {
            return definition.Kind switch
            {
                AggregateKind.Message => CompileDecodeMessage(definition, useGenerics),
                AggregateKind.Struct => CompileDecodeStruct(definition, useGenerics),
                _ => throw new InvalidOperationException(
                    $"invalid CompileDecode kind: {definition.Kind} in {definition}")
            };
        }

        private string CompileDecodeStruct(IDefinition definition, bool useGenerics)
        {
            var builder = new IndentedStringBuilder();
            int i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"{TypeName(field.Type)} field{i};");
                builder.AppendLine($"{CompileDecodeField(field.Type, $"field{i}")}");
                i++;
            }

            builder.AppendLine($"return new {(useGenerics ? "T" : definition.Name.ToPascalCase())} {{");
            builder.Indent(indentStep);
            i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"{field.Name.ToPascalCase()} = field{i++},");
            }
            builder.Dedent(indentStep);
            builder.AppendLine("};");
            return builder.ToString();
        }

        /// <summary>
        ///     Generate the body of the <c>DecodeFrom</c> function for the given <see cref="IDefinition"/>,
        ///     given that its "kind" is Message.
        /// </summary>
        /// <param name="definition">The message definition to generate code for.</param>
        /// <param name="useGenerics"></param>
        /// <returns>The generated C# <c>DecodeFrom</c> function body.</returns>
        private string CompileDecodeMessage(IDefinition definition, bool useGenerics)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine($"var record = new {(useGenerics ? "T" : definition.Name.ToPascalCase())}();");
            builder.AppendLine("var length = reader.ReadRecordLength();");
            builder.AppendLine("var end = unchecked((int) (reader.Position + length));");
            builder.AppendLine("while (true) {");
            builder.Indent(indentStep);
            builder.AppendLine("switch (reader.ReadByte()) {");
            builder.Indent(indentStep);

            // 0 case: end of message
            builder.AppendLine("case 0:");
            builder.Indent(indentStep);
            builder.AppendLine("return record;");
            builder.Dedent(indentStep);

            // cases for fields
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"case {field.ConstantValue}:");
                builder.Indent(indentStep);
                builder.AppendLine($"{CompileDecodeField(field.Type, $"record.{field.Name.ToPascalCase()}")}");
                builder.AppendLine("break;");
                builder.Dedent(indentStep);
            }

            // default case: unknown, skip to end of message
            builder.AppendLine("default:");
            builder.Indent(indentStep);
            builder.AppendLine("reader.Position = end;");
            builder.AppendLine("return record;");
            builder.Dedent(indentStep);

            // end switch:
            builder.Dedent(indentStep);
            builder.AppendLine("}");

            // end while:
            builder.Dedent(indentStep);
            builder.AppendLine("}");
            return builder.ToString();
        }


        private string CompileDecodeField(TypeBase type, string target, int depth = 0)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', depth * 2 * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = reader.ReadBytes();",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}var length{depth} = unchecked((int)reader.ReadUInt32());" + nl +
                    $"{tab}{target} = new {TypeName(at, $"length{depth}")};" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(at.MemberType)} x{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{target}[{i}] = x{depth};" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"{{" + nl +
                    $"{tab}var length{depth} = unchecked((int)reader.ReadUInt32());" + nl +
                    $"{tab}{target} = new {TypeName(mt)}(length{depth});" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(mt.KeyType)} k{depth};" + nl +
                    $"{tab}{tab}{TypeName(mt.ValueType)} v{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{target}.Add(k{depth}, v{depth});" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"{target} = reader.ReadByte() != 0;",
                    BaseType.Byte => $"{target} = reader.ReadByte();",
                    BaseType.UInt32 => $"{target} = reader.ReadUInt32();",
                    BaseType.Int32 => $"{target} = reader.ReadInt32();",
                    BaseType.Float32 => $"{target} = reader.ReadFloat32();",
                    BaseType.String => $"{target} = reader.ReadString();",
                    BaseType.Guid => $"{target} = reader.ReadGuid();",
                    BaseType.UInt16 => $"{target} = reader.ReadUInt16();",
                    BaseType.Int16 => $"{target} = reader.ReadInt16();",
                    BaseType.UInt64 => $"{target} = reader.ReadUInt64();",
                    BaseType.Int64 => $"{target} = reader.ReadInt64();",
                    BaseType.Float64 => $"{target} = reader.ReadFloat64();",
                    BaseType.Date => $"{target} = reader.ReadDate();",
                    _ => throw new ArgumentOutOfRangeException()
                },
                DefinedType dt when Schema.Definitions[dt.Name].Kind == AggregateKind.Enum =>
                    $"{target} = reader.ReadEnum<{dt.Name}>();",
                DefinedType dt => 
                    $"{target} = {(string.IsNullOrWhiteSpace(Schema.Namespace) ? string.Empty : $"{Schema.Namespace.ToPascalCase()}.")}{dt.Name.ToPascalCase()}.DecodeFrom(ref reader);",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        ///     Generates the body of various helper methods to encode the given <see cref="IDefinition"/>
        /// </summary>
        /// <param name="definition"></param>
        /// <returns></returns>
        public string CompileEncodeHelper(IDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine(GeneratedAttribute);
            builder.AppendLine(HotPath);
            builder.AppendLine($"public static byte[] Encode(Base{definition.Name.ToPascalCase()} record) {{");
            builder.Indent(indentStep);
            builder.AppendLine("var writer = BebopWriter.Create();");
            builder.AppendLine("EncodeInto(record, ref writer);");
            builder.AppendLine("return writer.ToArray();");
            builder.Dedent(indentStep);
            builder.AppendLine("}");
            builder.AppendLine("");
            builder.AppendLine(GeneratedAttribute);
            builder.AppendLine(HotPath);
            builder.AppendLine($"public byte[] Encode() {{");
            builder.Indent(indentStep);
            builder.AppendLine("var writer = BebopWriter.Create();");
            builder.AppendLine("EncodeInto(this, ref writer);");
            builder.AppendLine("return writer.ToArray();");
            builder.Dedent(indentStep);
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>
        ///     Generates the body of various helper methods to decode the given <see cref="IDefinition"/>
        /// </summary>
        /// <param name="definition"></param>
        /// <param name="bufferType"></param>
        /// <returns></returns>
        public string CompileDecodeHelper(IDefinition definition, string bufferType)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine(GeneratedAttribute);
            builder.AppendLine(HotPath);
            builder.AppendLine($"public static T DecodeAs<T>({bufferType} record) where T : Base{definition.Name.ToPascalCase()}, new() {{");
            builder.Indent(indentStep);
            builder.AppendLine("var reader = BebopReader.From(record);");
            builder.AppendLine("return DecodeFrom<T>(ref reader);");
            builder.Dedent(indentStep);
            builder.AppendLine("}");
            builder.AppendLine("");
            builder.AppendLine(GeneratedAttribute);
            builder.AppendLine(HotPath);
            builder.AppendLine($"public static {definition.Name.ToPascalCase()} Decode({bufferType} record) {{");
            builder.Indent(indentStep);
            builder.AppendLine("var reader = BebopReader.From(record);");
            builder.AppendLine($"return DecodeFrom(ref reader);");
            builder.Dedent(indentStep);
            builder.AppendLine("}");
            builder.AppendLine("");

            return builder.ToString();
        }

        /// <summary>
        ///     Generate the body of the <c>EncodeTo</c> function for the given <see cref="IDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated C# <c>EncodeTo</c> function body.</returns>
        public string CompileEncode(IDefinition definition)
        {
            return definition.Kind switch
            {
                AggregateKind.Message => CompileEncodeMessage(definition),
                AggregateKind.Struct => CompileEncodeStruct(definition),
                _ => throw new InvalidOperationException(
                    $"invalid CompileEncode kind: {definition.Kind} in {definition}")
            };
        }

        private string CompileEncodeMessage(IDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine($"var pos = writer.ReserveRecordLength();");
            builder.AppendLine($"var start = writer.Length;");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedAttribute is not null)
                {
                    continue;
                }
                builder.AppendLine("");

                var isNullableType = IsNullableType(field.Type);

                builder.AppendLine($"if (record.{field.Name.ToPascalCase()} is not null) {{");
                builder.Indent(indentStep);
                builder.AppendLine($"writer.WriteByte({field.ConstantValue});");
                builder.AppendLine(isNullableType
                    ? $"{CompileEncodeField(field.Type, $"record.{field.Name.ToPascalCase()}.Value")}"
                    : $"{CompileEncodeField(field.Type, $"record.{field.Name.ToPascalCase()}")}");
                builder.Dedent(indentStep);
                builder.AppendLine("}");
            }
            builder.AppendLine("writer.WriteByte(0);");
            builder.AppendLine("var end = writer.Length;");
            builder.AppendLine("writer.FillRecordLength(pos, unchecked((uint) unchecked(end - start)));");
            return builder.ToString();
        }

        private string CompileEncodeStruct(IDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"{CompileEncodeField(field.Type, $"record.{field.Name.ToPascalCase()}")}");
            }
            return builder.ToString();
        }

        private bool IsNullableType(TypeBase type)
        {
            return type switch
            {
                DefinedType dt when Schema.Definitions[dt.Name].Kind == AggregateKind.Enum => true,
                DefinedType => false,
                ArrayType => false,
                MapType => false,
                ScalarType st => st.BaseType switch
                {
                    BaseType.String => false,
                    BaseType.Guid =>  true,
                    BaseType.Date => true,
                    _ => true
                },
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }


        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"writer.WriteBytes({target});",
                ArrayType at when at.IsFloat32s() => $"writer.WriteFloat32s({target});",
                ArrayType at when at.IsFloat64s() => $"writer.WriteFloat64s({target});",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}var length{depth} = unchecked((uint){target}.Length);" + nl +
                    $"{tab}writer.WriteUInt32(length{depth});" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{CompileEncodeField(at.MemberType, $"{target}[{i}]", depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"writer.WriteUInt32(unchecked((uint){target}.Count));" + nl +
                    $"foreach (var kv{depth} in {target}) {{" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"kv{depth}.Key", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"kv{depth}.Value", depth + 1, indentDepth + 1)}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"writer.WriteByte({target});",
                    BaseType.Byte => $"writer.WriteByte({target});",
                    BaseType.UInt32 => $"writer.WriteUInt32({target});",
                    BaseType.Int32 => $"writer.WriteInt32({target});",
                    BaseType.Float32 => $"writer.WriteFloat32({target});",
                    BaseType.Float64 => $"writer.WriteFloat64({target});",
                    BaseType.String => $"writer.WriteString({target});",
                    BaseType.Guid => $"writer.WriteGuid({target});",
                    BaseType.UInt16 => $"writer.WriteUInt16({target});",
                    BaseType.Int16 => $"writer.WriteInt16({target});",
                    BaseType.UInt64 => $"writer.WriteUInt64({target});",
                    BaseType.Int64 => $"writer.WriteInt64({target});",
                    BaseType.Date => $"writer.WriteDate({target});",
                    _ => throw new ArgumentOutOfRangeException()
                },
                DefinedType dt when Schema.Definitions[dt.Name].Kind == AggregateKind.Enum =>
                    $"writer.WriteEnum<{dt.Name}>({target});",
                DefinedType dt =>
                    $"{(string.IsNullOrWhiteSpace(Schema.Namespace) ? string.Empty : $"{Schema.Namespace.ToPascalCase()}.")}{dt.Name.ToPascalCase()}.EncodeInto({target}, ref writer);",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }
    }
}
