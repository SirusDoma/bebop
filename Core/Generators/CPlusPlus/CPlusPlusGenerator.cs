using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;
using Core.Meta.Extensions;

namespace Core.Generators.CPlusPlus
{
    public class CPlusPlusGenerator : BaseGenerator
    {
        const int indentStep = 2;

        public CPlusPlusGenerator() : base() { }

        private string FormatDocumentation(string documentation, int spaces)
        {
            var builder = new IndentedStringBuilder();
            builder.Indent(spaces);
            foreach (var line in documentation.GetLines())
            {
                builder.AppendLine($"/// {line}");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Generate the body of the <c>encode</c> function for the given <see cref="RecordDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>encode</c> function body.</returns>
        public string CompileEncode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileEncodeMessage(d),
                StructDefinition d => CompileEncodeStruct(d),
                UnionDefinition d => CompileEncodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileEncode kind: {definition}"),
            };
        }

        private string CompileEncodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            builder.AppendLine($"const auto pos = writer.reserveMessageLength();");
            builder.AppendLine($"const auto start = writer.length();");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedDecorator != null)
                {
                    continue;
                }
                builder.AppendLine($"if (message.{field.Name}.has_value()) {{");
                builder.AppendLine($"    writer.writeByte({field.ConstantValue});");
                builder.AppendLine($"    {CompileEncodeField(field.Type, $"message.{field.Name}.value()", 0, 1, true)}");
                builder.AppendLine($"}}");
            }
            builder.AppendLine("writer.writeByte(0);");
            builder.AppendLine("const auto end = writer.length();");
            builder.AppendLine("writer.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileEncodeField(field.Type, $"message.{field.Name}"));
            }
            return builder.ToString();
        }

        private string CompileEncodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            builder.AppendLine($"const auto pos = writer.reserveMessageLength();");
            builder.AppendLine($"const std::uint8_t discriminator = message.variant.index() + 1;");
            builder.AppendLine($"writer.writeByte(discriminator);");
            builder.AppendLine($"const auto start = writer.length();");
            builder.AppendLine($"switch (discriminator) {{");
            int i = 0;
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($" case {branch.Discriminator}:");
                builder.AppendLine($"    {branch.Definition.Name}::encodeInto(std::get<{i++}>(message.variant), writer);");
                builder.AppendLine($"    break;");
            }
            builder.AppendLine($"}}");
            builder.AppendLine("const auto end = writer.length();");
            builder.AppendLine("writer.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0, bool isOptional = false)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"writer.writeBytes({target});",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = {target}.size();" + nl +
                    $"{tab}writer.writeUint32(length{depth});" + nl +
                    $"{tab}for (const auto& {i} : {target}) {{" + nl +
                    $"{tab}{tab}{CompileEncodeField(at.MemberType, i, depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"writer.writeUint32({target}.size());" + nl +
                    $"for (const auto& e{depth} : {target}) {{" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"e{depth}.first", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"e{depth}.second", depth + 1, indentDepth + 1)}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"writer.writeBool({target});",
                    BaseType.Byte => $"writer.writeByte({target});",
                    BaseType.UInt16 => $"writer.writeUint16({target});",
                    BaseType.Int16 => $"writer.writeInt16({target});",
                    BaseType.UInt32 => $"writer.writeUint32({target});",
                    BaseType.Int32 => $"writer.writeInt32({target});",
                    BaseType.UInt64 => $"writer.writeUint64({target});",
                    BaseType.Int64 => $"writer.writeInt64({target});",
                    BaseType.Float32 => $"writer.writeFloat32({target});",
                    BaseType.Float64 => $"writer.writeFloat64({target});",
                    BaseType.String => $"writer.writeString({target});",
                    BaseType.Guid => $"writer.writeGuid({target});",
                    BaseType.Date => $"writer.writeDate({target});",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                },
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    CompileEncodeField(ed.ScalarType, $"static_cast<{TypeName(ed.ScalarType)}>({target})", depth, indentDepth),
                DefinedType dt when isOptional => $"::bebop::encodeInto<{Config.Namespace}::{dt.Name}>({target}, writer);",
                DefinedType dt => $"{dt.Name}::encodeInto({target}, writer);",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="RecordDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>decode</c> function body.</returns>
        public string CompileDecode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileDecodeMessage(d),
                StructDefinition d => CompileDecodeStruct(d),
                UnionDefinition d => CompileDecodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileDecode kind: {definition}"),
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="MessageDefinition"/>.
        /// </summary>
        /// <param name="definition">The message definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>decode</c> function body.</returns>
        private string CompileDecodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            builder.AppendLine("const auto length = reader.readLengthPrefix();");
            builder.AppendLine("const auto end = reader.pointer() + length;");
            builder.AppendLine("while (true) {");
            builder.Indent(2);
            builder.AppendLine("switch (reader.readByte()) {");
            builder.AppendLine("    case 0:");
            builder.AppendLine("        return reader.bytesRead();");
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"    case {field.ConstantValue}:");
                builder.AppendLine($"        {CompileDecodeField(field.Type, $"target.{field.Name}", 0, 2, true)}");
                builder.AppendLine("        break;");
            }
            builder.AppendLine("    default:");
            builder.AppendLine("        reader.seek(end);");
            builder.AppendLine("        return reader.bytesRead();");
            builder.AppendLine("}");
            builder.Dedent(2);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string CompileDecodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            int i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileDecodeField(field.Type, $"target.{field.Name}", 0, 0, false));
                i++;
            }
            // var args = string.Join(", ", definition.Fields.Select((field, i) => $"field{i}"));
            // builder.AppendLine($"return {definition.Name} {{ {args} }};");
            return builder.ToString();
        }

        private string CompileDecodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(8);
            builder.AppendLine("const auto length = reader.readLengthPrefix();");
            builder.AppendLine("const auto end = reader.pointer() + length + 1;");
            builder.AppendLine("switch (reader.readByte()) {");
            int i = 0;
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($"    case {branch.Discriminator}:");
                builder.AppendLine($"        target.variant.emplace<{i}>();");
                builder.AppendLine($"        {branch.Definition.Name}::decodeInto(reader, std::get<{i}>(target.variant));");
                builder.AppendLine("        break;");
                i++;
            }
            builder.AppendLine("    default:");
            builder.AppendLine("        reader.seek(end); // do nothing?");
            builder.AppendLine("        return reader.bytesRead();");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string ReadBaseType(BaseType baseType)
        {
            return baseType switch
            {
                BaseType.Bool => "reader.readBool()",
                BaseType.Byte => "reader.readByte()",
                BaseType.UInt32 => "reader.readUint32()",
                BaseType.Int32 => "reader.readInt32()",
                BaseType.Float32 => "reader.readFloat32()",
                BaseType.String => "reader.readString()",
                BaseType.Guid => "reader.readGuid()",
                BaseType.UInt16 => "reader.readUint16()",
                BaseType.Int16 => "reader.readInt16()",
                BaseType.UInt64 => "reader.readUint64()",
                BaseType.Int64 => "reader.readInt64()",
                BaseType.Float64 => "reader.readFloat64()",
                BaseType.Date => "reader.readDate()",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private string CompileDecodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0, bool isOptional = false)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            var dot = isOptional ? "->" : ".";
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = reader.readBytes();",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = reader.readUint32();" + nl +
                    $"{tab}{target} = {TypeName(at)}();" + nl +
                    $"{tab}{target}{dot}reserve(length{depth});" + nl +
                    $"{tab}for (size_t {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(at.MemberType)} x{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1, indentDepth + 2, false)}" + nl +
                    $"{tab}{tab}{target}{dot}push_back(x{depth});" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = reader.readUint32();" + nl +
                    $"{tab}{target} = {TypeName(mt)}();" + nl +
                    $"{tab}for (size_t {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(mt.KeyType)} k{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1, indentDepth + 2, false)}" + nl +
                    $"{tab}{tab}{TypeName(mt.ValueType)}& v{depth} = {target}{dot}operator[](k{depth});" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1, indentDepth + 2, false)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                ScalarType st => $"{target} = {ReadBaseType(st.BaseType)};",
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    $"{target} = static_cast<{Config.Namespace}::{dt.Name}>({ReadBaseType(ed.BaseType)});",
                DefinedType dt when isOptional => $"::bebop::decodeInto<{Config.Namespace}::{dt.Name}>(reader, {target});",
                DefinedType dt => $"{dt.Name}::decodeInto(reader, {target});",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        /// Generate a CPlusPlus type name for the given <see cref="TypeBase"/>.
        /// </summary>
        /// <param name="type">The field type to generate code for.</param>
        /// <returns>The CPlusPlus type name.</returns>
        private string TypeName(in TypeBase type)
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "bool",
                        BaseType.Byte => "std::uint8_t",
                        BaseType.UInt16 => "std::uint16_t",
                        BaseType.Int16 => "std::int16_t",
                        BaseType.UInt32 => "std::uint32_t",
                        BaseType.Int32 => "std::int32_t",
                        BaseType.UInt64 => "std::uint64_t",
                        BaseType.Int64 => "std::int64_t",
                        BaseType.Float32 => "float",
                        BaseType.Float64 => "double",
                        BaseType.String => "std::string",
                        BaseType.Guid => "::bebop::Guid",
                        BaseType.Date => "::bebop::TickDuration",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                // case ArrayType at when at.IsBytes():
                //     return "std::vector<std::uint8_t>";
                case ArrayType at:
                    return $"std::vector<{TypeName(at.MemberType)}>";
                case MapType mt:
                    return $"std::map<{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}>";
                case DefinedType dt:
                    return $"{Config.Namespace}::{dt.Name}";
            }
            throw new InvalidOperationException($"GetTypeName: {type}");
        }

        private string Optional(TypeBase type)
        {
            return type is not DefinedType dt || dt.IsEnum(Schema) ? $"std::optional<{TypeName(type)}>" : $"::bebop::Optional<{TypeName(type)}>";
        }

        private static string EscapeStringLiteral(string value)
        {
            return $@"""{value.EscapeString()}""";
        }

        private string EmitLiteral(Literal literal)
        {
            return literal switch
            {
                BoolLiteral bl => bl.Value ? "true" : "false",
                IntegerLiteral il => il.Value,
                FloatLiteral fl when fl.Value == "inf" => $"std::numeric_limits<{TypeName(literal.Type)}>::infinity()",
                FloatLiteral fl when fl.Value == "-inf" => $"-std::numeric_limits<{TypeName(literal.Type)}>::infinity()",
                FloatLiteral fl when fl.Value == "nan" => $"std::numeric_limits<{TypeName(literal.Type)}>::quiet_NaN()",
                FloatLiteral fl => fl.Value,
                StringLiteral sl => EscapeStringLiteral(sl.Value),
                GuidLiteral gl => $"::bebop::Guid::fromString(\"{gl.Value}\")",
                _ => throw new ArgumentOutOfRangeException(literal.ToString()),
            };
        }

        /// <summary>
        /// Generate code for a Bebop schema.
        /// </summary>
        /// <returns>The generated code.</returns>
        public override ValueTask<string> Compile(BebopSchema schema, GeneratorConfig config, CancellationToken cancellationToken = default)
        {
            Schema = schema;
            Config = config;
            var builder = new StringBuilder();
            string outputPath = Path.GetDirectoryName(Config.OutFile)!;

            if (Config.EmitNotice)
            {
                builder.AppendLine(GeneratorUtils.GetXmlAutoGeneratedNotice());
            }
            builder.AppendLine("#pragma once");
            builder.AppendLine("#include <cstddef>");
            builder.AppendLine("#include <cstdint>");
            builder.AppendLine("#include <map>");
            builder.AppendLine("#include <memory>");
            builder.AppendLine("#include <optional>");
            builder.AppendLine("#include <string>");
            builder.AppendLine("#include <variant>");
            builder.AppendLine("#include <vector>");
            builder.AppendLine("#include \"bebop.hpp\"");

            var baseBuilder = new StringBuilder(builder.ToString());
            builder.AppendLine("");

            var (definitions, cyclics) = Schema.SortedDefinitions();
            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.AppendLine($"namespace {Config.Namespace} {{");
                builder.AppendLine("");
            }

            foreach (Definition? definition in cyclics.Select(cyclic => Schema.Definitions[cyclic.Key]))
            {
                switch (definition)
                {
                    case RecordDefinition td:
                        builder.AppendLine($"struct {td.Name};");
                        break;
                    case EnumDefinition:
                    case ConstDefinition:
                    case ServiceDefinition:
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported definition {definition}");
                }
            }

            foreach (var definition in definitions)
            {
                var definitionBuilder = new StringBuilder();
                var includeBuilder = new StringBuilder(baseBuilder.ToString());

                if (!string.IsNullOrWhiteSpace(definition.Documentation))
                {
                    definitionBuilder.Append(FormatDocumentation(definition.Documentation, 0));
                }
                switch (definition)
                {
                    case EnumDefinition ed:
                        definitionBuilder.AppendLine($"enum class {definition.Name} : {TypeName(ed.ScalarType)} {{");
                        for (var i = 0; i < ed.Members.Count; i++)
                        {
                            var field = ed.Members.ElementAt(i);
                            if (!string.IsNullOrWhiteSpace(field.Documentation))
                            {
                                definitionBuilder.Append(FormatDocumentation(field.Documentation, 2));
                            }
                            if (field.DeprecatedDecorator is not null && field.DeprecatedDecorator.TryGetValue("reason", out var reason))
                            {
                                definitionBuilder.AppendLine($"    /// @deprecated {reason}");
                            }
                            definitionBuilder.AppendLine($"    {field.Name} = {field.ConstantValue},");
                        }
                        definitionBuilder.AppendLine("};");
                        definitionBuilder.AppendLine("");
                        break;
                    case RecordDefinition td:
                        definitionBuilder.AppendLine($"struct {td.Name} {{");
                        definitionBuilder.AppendLine($"    static constexpr size_t minimalEncodedSize = {td.MinimalEncodedSize(Schema)};");
                        if (td.OpcodeDecorator is not null && td.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                        {
                            definitionBuilder.AppendLine($"    static const std::uint32_t opcode = {fourcc};");
                            definitionBuilder.AppendLine("");
                        }

                        if (td.DiscriminatorInParent != null)
                        {
                            definitionBuilder.AppendLine($"    static constexpr std::uint32_t discriminator = {td.DiscriminatorInParent};");
                            definitionBuilder.AppendLine("");
                        }

                        if (td is FieldsDefinition fd)
                        {
                            var isMessage = fd is MessageDefinition;
                            for (var i = 0; i < fd.Fields.Count; i++)
                            {
                                var field = fd.Fields.ElementAt(i);
                                if (!string.IsNullOrWhiteSpace(field.Documentation))
                                {
                                    definitionBuilder.Append(FormatDocumentation(field.Documentation, 2));
                                }
                                if (field.DeprecatedDecorator is not null && field.DeprecatedDecorator.TryGetValue("reason", out var reason))
                                {
                                    definitionBuilder.AppendLine($"    /// @deprecated {reason}");
                                }

                                definitionBuilder.AppendLine($"    {(isMessage ? Optional(field.Type) : TypeName(field.Type))} {field.Name};");
                            }
                            definitionBuilder.AppendLine("");
                        }
                        else if (td is UnionDefinition ud)
                        {
                            includeBuilder.AppendLine();
                            foreach (string branch in ud.Branches.Select(b => b.Definition.Name))
                            {
                                includeBuilder.AppendLine($"#include \"{branch}.g.hpp\"");
                            }

                            var types = ud.Branches.Select(b => $"{Config.Namespace}::{b.Definition.Name}").ToList();
                            definitionBuilder.AppendLine($"    using Union = std::variant<{string.Join(", ", types)}>;");
                            definitionBuilder.AppendLine($"    Union variant;");
                            definitionBuilder.AppendLine();

                            definitionBuilder.AppendLine($"    {td.Name}() = default;");
                            foreach (string t in types.Where(t => !string.IsNullOrEmpty(t)))
                            {
                                definitionBuilder.AppendLine($"    {td.Name}({t} value) : variant(std::move(value)) {{}}");
                            }

                            definitionBuilder.AppendLine();
                            definitionBuilder.AppendLine("    template<typename T>");
                            definitionBuilder.AppendLine("    bool is() const {{");
                            definitionBuilder.AppendLine("        return std::holds_alternative<T>(variant);");
                            definitionBuilder.AppendLine("    }}");
                            definitionBuilder.AppendLine();
                            definitionBuilder.AppendLine("    template<typename T>");
                            definitionBuilder.AppendLine("    T& get() const {{");
                            definitionBuilder.AppendLine("        if (!is<T>()) {{");
                            definitionBuilder.AppendLine("            throw std::bad_variant_access();");
                            definitionBuilder.AppendLine("        }}");
                            definitionBuilder.AppendLine("        return std::get<T>(variant);");
                            definitionBuilder.AppendLine("    }}");
                            definitionBuilder.AppendLine();
                        }
                        else
                        {
                            throw new InvalidOperationException($"unsupported definition {td}");
                        }

                        definitionBuilder.AppendLine($"    static size_t encodeInto(const {td.Name}& message, std::vector<std::uint8_t>& targetBuffer) {{");
                        definitionBuilder.AppendLine("        ::bebop::Writer writer{targetBuffer};");
                        definitionBuilder.AppendLine($"        return {td.Name}::encodeInto(message, writer);");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    template<typename T = ::bebop::Writer>");
                        definitionBuilder.AppendLine($"    static size_t encodeInto(const {td.Name}& message, T& writer) {{");
                        definitionBuilder.AppendLine("        size_t before = writer.length();");
                        definitionBuilder.Append(CompileEncode(td));
                        definitionBuilder.AppendLine("        size_t after = writer.length();");
                        definitionBuilder.AppendLine("        return after - before;");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    size_t encodeInto(std::vector<std::uint8_t>& targetBuffer) {{ return {td.Name}::encodeInto(*this, targetBuffer); }}");
                        definitionBuilder.AppendLine($"    size_t encodeInto(::bebop::Writer& writer) {{ return {td.Name}::encodeInto(*this, writer); }}");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static {td.Name} decode(const std::uint8_t* sourceBuffer, size_t sourceBufferSize) {{");
                        definitionBuilder.AppendLine($"        {td.Name} result;");
                        definitionBuilder.AppendLine($"        {td.Name}::decodeInto(sourceBuffer, sourceBufferSize, result);");
                        definitionBuilder.AppendLine($"        return result;");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static {td.Name} decode(std::vector<std::uint8_t>& sourceBuffer) {{");
                        definitionBuilder.AppendLine($"        return {td.Name}::decode(sourceBuffer.data(), sourceBuffer.size());");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static {td.Name} decode(::bebop::Reader& reader) {{");
                        definitionBuilder.AppendLine($"        {td.Name} result;");
                        definitionBuilder.AppendLine($"        {td.Name}::decodeInto(reader, result);");
                        definitionBuilder.AppendLine($"        return result;");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static size_t decodeInto(const std::uint8_t* sourceBuffer, size_t sourceBufferSize, {td.Name}& target) {{");
                        definitionBuilder.AppendLine("        ::bebop::Reader reader{sourceBuffer, sourceBufferSize};");
                        definitionBuilder.AppendLine($"        return {td.Name}::decodeInto(reader, target);");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static size_t decodeInto(std::vector<std::uint8_t>& sourceBuffer, {td.Name}& target) {{");
                        definitionBuilder.AppendLine($"        return {td.Name}::decodeInto(sourceBuffer.data(), sourceBuffer.size(), target);");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine($"    static size_t decodeInto(::bebop::Reader& reader, {td.Name}& target) {{");
                        definitionBuilder.Append(CompileDecode(td));
                        definitionBuilder.AppendLine("        return reader.bytesRead();");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("");
                        definitionBuilder.AppendLine("    size_t byteCount() {");
                        definitionBuilder.AppendLine("        ::bebop::ByteCounter counter{};");
                        definitionBuilder.AppendLine($"        {td.Name}::encodeInto<::bebop::ByteCounter>(*this, counter);");
                        definitionBuilder.AppendLine("        return counter.length();");
                        definitionBuilder.AppendLine("    }");
                        definitionBuilder.AppendLine("};");
                        definitionBuilder.AppendLine("");
                        break;
                    case ConstDefinition cd:
                        definitionBuilder.AppendLine($"const {TypeName(cd.Value.Type)} {cd.Name} = {EmitLiteral(cd.Value)};");
                        definitionBuilder.AppendLine("");
                        break;
                    case ServiceDefinition:
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported definition {definition}");
                }

                builder.AppendLine(definitionBuilder.ToString());
                var forwardBuilder = new StringBuilder();

                if (cyclics.TryGetValue(definition.Name, out var cyc))
                {
                    definitionBuilder.AppendLine();

                    bool hasInclude = false;
                    bool hasForward = false;
                    if (definition is MessageDefinition md)
                    {
                        var deps = md.Dependencies().Where(d => cyclics.ContainsKey(d)).ToList();
                        foreach (string dep in deps)
                        {
                            bool forward = false;
                            foreach (var type in md.Fields.Where(f => f.Type is DefinedType && !f.Type.IsEnum(Schema)).Select(f => f.Type as DefinedType))
                            {
                                if (type == null)
                                    continue;

                                if (type.IsEnum(Schema) || type.IsUnion(Schema))
                                {
                                    forward = false;
                                    continue;
                                }

                                if (type.Name == dep)
                                {
                                    forward = true;
                                    break;
                                }
                            }

                            if (forward)
                            {
                                if (!hasForward)
                                {
                                    definitionBuilder.AppendLine("#include \"bebop.inl\"");
                                    definitionBuilder.AppendLine();
                                }

                                forwardBuilder.AppendLine($"struct {dep};");
                                hasForward = true;
                            }
                            else
                            {
                                if (!hasInclude)
                                {
                                    includeBuilder.AppendLine();
                                }

                                includeBuilder.AppendLine($"#include \"{dep}.g.hpp\"");
                                hasInclude = true;
                            }
                        }
                    }
                    else if (definition.Dependencies().Any(d => cyc.Contains(d)))
                    {
                        includeBuilder.AppendLine("// WARNING: Cyclics dependencies are found in a struct:");
                        foreach (var c in cyc)
                            includeBuilder.AppendLine($"//   {c}");
                    }
                }
                else if (definition is FieldsDefinition)
                {
                    includeBuilder.AppendLine();
                    foreach (string dep in definition.Dependencies())
                    {
                        includeBuilder.AppendLine($"#include \"{dep}.g.hpp\"");
                    }
                }

                includeBuilder.AppendLine();

                var encoding = new UTF8Encoding(false);
                File.WriteAllBytes(Path.Join(outputPath, $"{definition.Name}.g.hpp"), encoding.GetBytes($"{includeBuilder}{forwardBuilder}{definitionBuilder}"));
            }

            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.AppendLine($"}} // namespace {Config.Namespace}");
                builder.AppendLine("");
            }

            var inlBuilder = new StringBuilder();
            inlBuilder.AppendLine("#pragma once");
            inlBuilder.AppendLine("#include \"bebop.hpp\"");
            inlBuilder.AppendLine();
            inlBuilder.AppendLine("namespace bebop {");
            inlBuilder.AppendLine("    template<typename T, typename W>");
            inlBuilder.AppendLine("    size_t encodeInto(const T& message, W& writer) {");
            inlBuilder.AppendLine("        return T::encodeInto(message, writer);");
            inlBuilder.AppendLine("    }");
            inlBuilder.AppendLine();
            inlBuilder.AppendLine("    template<typename T>");
            inlBuilder.AppendLine("    size_t decodeInto(::bebop::Reader& reader, ::bebop::Optional<T>& target) {");
            inlBuilder.AppendLine("        return T::decodeInto(reader, target.value_or_emplace());");
            inlBuilder.AppendLine("    }");
            inlBuilder.AppendLine("}");
            inlBuilder.AppendLine();
            File.WriteAllBytes(Path.Join(outputPath, "bebop.inl"), new UTF8Encoding(false).GetBytes(inlBuilder.ToString()));

            builder.AppendLine("#include \"bebop.inl\"");
            // Save this somewhere?

            builder = new StringBuilder();
            builder.AppendLine(baseBuilder.ToString());
            foreach (var definition in definitions)
            {
                builder.AppendLine($"#include \"{definition.Name}.g.hpp\"");
            }

            return ValueTask.FromResult(builder.ToString());
        }

        private const string _bytePattern = @"(?:"")?(\b(1?[0-9]{1,2}|2[0-4][0-9]|25[0-5])\b)(?:"")?";
        private static readonly Regex _majorRegex = new Regex($@"(?<=BEBOPC_VER_MAJOR\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _minorRegex = new Regex($@"(?<=BEBOPC_VER_MINOR\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _patchRegex = new Regex($@"(?<=BEBOPC_VER_PATCH\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _informationalRegex = new Regex($@"(?<=BEBOPC_VER_INFO\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);

        private AuxiliaryFile LoadAuxiliaryFile(string fileName)
        {
            var assembly = Assembly.GetEntryAssembly()!;
            var runtime = assembly.GetManifestResourceNames()!.FirstOrDefault(n => n.Contains(fileName))!;

            using var stream = assembly.GetManifestResourceStream(runtime)!;
            using var reader = new StreamReader(stream);
            var builder = new StringBuilder();
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is string line)
                {
                    if (_majorRegex.IsMatch(line)) line = _majorRegex.Replace(line, DotEnv.Generated.Environment.Major.ToString());
                    if (_minorRegex.IsMatch(line)) line = _minorRegex.Replace(line, DotEnv.Generated.Environment.Minor.ToString());
                    if (_patchRegex.IsMatch(line)) line = _patchRegex.Replace(line, DotEnv.Generated.Environment.Patch.ToString());
                    if (_informationalRegex.IsMatch(line)) line = _informationalRegex.Replace(line, $"\"{DotEnv.Generated.Environment.Version}\"");
                    builder.AppendLine(line);

                }
            }
            var encoding = new UTF8Encoding(false);
            return new AuxiliaryFile(fileName, encoding.GetBytes(builder.ToString()));
        }

        public override AuxiliaryFile? GetAuxiliaryFile()
        {
            var assembly = Assembly.GetEntryAssembly()!;
            var runtime = assembly.GetManifestResourceNames()!.FirstOrDefault(n => n.Contains("bebop.hpp"))!;

            using var stream = assembly.GetManifestResourceStream(runtime)!;
            using var reader = new StreamReader(stream);
            var builder = new StringBuilder();
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is string line)
                {
                    if (_majorRegex.IsMatch(line)) line = _majorRegex.Replace(line, DotEnv.Generated.Environment.Major.ToString());
                    if (_minorRegex.IsMatch(line)) line = _minorRegex.Replace(line, DotEnv.Generated.Environment.Minor.ToString());
                    if (_patchRegex.IsMatch(line)) line = _patchRegex.Replace(line, DotEnv.Generated.Environment.Patch.ToString());
                    if (_informationalRegex.IsMatch(line)) line = _informationalRegex.Replace(line, $"\"{DotEnv.Generated.Environment.Version}\"");
                    builder.AppendLine(line);

                }
            }
            var encoding = new UTF8Encoding(false);
            return new AuxiliaryFile("bebop.hpp", encoding.GetBytes(builder.ToString()));
        }

        public override void WriteAuxiliaryFile(string outputPath)
        {
            var auxiliary = GetAuxiliaryFile();
            if (auxiliary is not null)
            {
                File.WriteAllBytes(Path.Join(outputPath, auxiliary.Name), auxiliary.Content);
            }

            // This is not working
            // var inlineAuxiliary = LoadAuxiliaryFile("bebop.inl");
            // File.WriteAllBytes(Path.Join(outputPath, auxiliary.Name), auxiliary.Content);
        }

        public override string Alias { get => "cpp"; set => throw new NotImplementedException(); }
        public override string Name { get => "C++"; set => throw new NotImplementedException(); }
    }
}
