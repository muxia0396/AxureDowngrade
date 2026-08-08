using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

internal static class Program
{
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = BuildOpCodes();
    private static string _axureDirectory = "";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Axure9IlDump <Axure9 directory> <method token hex>");
            return 2;
        }

        _axureDirectory = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAxureAssembly;
        try
        {
            var assembly = Assembly.LoadFrom(Path.Combine(_axureDirectory, "AxureRP9.exe"));
            var token = Convert.ToInt32(args[1], 16);
            var method = assembly.ManifestModule.ResolveMethod(token);
            Console.WriteLine("{0:X8} {1}", token, method);
            Dump(method);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Dump(MethodBase method)
    {
        var body = method.GetMethodBody();
        var bytes = body.GetILAsByteArray();
        var module = method.Module;
        var position = 0;
        while (position < bytes.Length)
        {
            var instructionOffset = position;
            ushort value = bytes[position++];
            if (value == 0xFE)
            {
                value = (ushort)(0xFE00 | bytes[position++]);
            }
            var opcode = OpCodesByValue[value];
            var operand = ReadOperand(bytes, ref position, opcode.OperandType, module);
            Console.WriteLine(
                "IL_{0:X4}: {1,-12} {2}",
                instructionOffset,
                opcode.Name,
                operand);
        }
    }

    private static string ReadOperand(
        byte[] bytes,
        ref int position,
        OperandType operandType,
        Module module)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return "";
            case OperandType.ShortInlineI:
                return ((sbyte)bytes[position++]).ToString();
            case OperandType.InlineI:
                return ReadInt32(bytes, ref position).ToString();
            case OperandType.InlineI8:
                var int64 = BitConverter.ToInt64(bytes, position);
                position += 8;
                return int64.ToString();
            case OperandType.ShortInlineR:
                var single = BitConverter.ToSingle(bytes, position);
                position += 4;
                return single.ToString();
            case OperandType.InlineR:
                var number = BitConverter.ToDouble(bytes, position);
                position += 8;
                return number.ToString();
            case OperandType.ShortInlineVar:
                return "V_" + bytes[position++];
            case OperandType.InlineVar:
                var variable = BitConverter.ToUInt16(bytes, position);
                position += 2;
                return "V_" + variable;
            case OperandType.ShortInlineBrTarget:
                var shortDelta = (sbyte)bytes[position++];
                return "IL_" + (position + shortDelta).ToString("X4");
            case OperandType.InlineBrTarget:
                var delta = ReadInt32(bytes, ref position);
                return "IL_" + (position + delta).ToString("X4");
            case OperandType.InlineString:
                return Quote(module.ResolveString(ReadInt32(bytes, ref position)));
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineType:
            case OperandType.InlineTok:
                var token = ReadInt32(bytes, ref position);
                try
                {
                    return string.Format("{0:X8} {1}", token, module.ResolveMember(token));
                }
                catch
                {
                    return token.ToString("X8");
                }
            case OperandType.InlineSig:
                return ReadInt32(bytes, ref position).ToString("X8");
            case OperandType.InlineSwitch:
                var count = ReadInt32(bytes, ref position);
                var baseOffset = position + (count * 4);
                var targets = new string[count];
                for (var index = 0; index < count; index++)
                {
                    targets[index] =
                        "IL_" + (baseOffset + ReadInt32(bytes, ref position)).ToString("X4");
                }
                return string.Join(", ", targets);
            default:
                throw new NotSupportedException(operandType.ToString());
        }
    }

    private static int ReadInt32(byte[] bytes, ref int position)
    {
        var value = BitConverter.ToInt32(bytes, position);
        position += 4;
        return value;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static Dictionary<ushort, OpCode> BuildOpCodes()
    {
        var result = new Dictionary<ushort, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            var opcode = (OpCode)field.GetValue(null);
            result[(ushort)opcode.Value] = opcode;
        }
        return result;
    }

    private static Assembly ResolveAxureAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        var dllPath = Path.Combine(_axureDirectory, name + ".dll");
        if (File.Exists(dllPath))
        {
            return Assembly.LoadFrom(dllPath);
        }
        var exePath = Path.Combine(_axureDirectory, name + ".exe");
        return File.Exists(exePath) ? Assembly.LoadFrom(exePath) : null;
    }
}
