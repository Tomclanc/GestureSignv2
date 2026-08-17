using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GestureSign.Shared
{
    internal static class KandoExecutableCompatibility
    {
        private const ushort ImageFileMachineI386 = 0x014c;
        private const ushort ImageFileMachineAmd64 = 0x8664;
        private const ushort ImageFileMachineArm64 = 0xaa64;

        public static bool IsSupportedOnCurrentOperatingSystem(string executablePath, out string reason)
        {
            try
            {
                var machine = ReadMachine(executablePath);
                var osArchitecture = RuntimeInformation.OSArchitecture;
                var supported = osArchitecture switch
                {
                    Architecture.X64 => machine is ImageFileMachineAmd64 or ImageFileMachineI386,
                    Architecture.Arm64 => machine is ImageFileMachineArm64 or ImageFileMachineAmd64 or ImageFileMachineI386,
                    Architecture.X86 => machine == ImageFileMachineI386,
                    _ => false
                };

                reason = supported
                    ? string.Empty
                    : $"Kando architecture 0x{machine:X4} is not supported on {osArchitecture} Windows. Executable={executablePath}";
                return supported;
            }
            catch (Exception ex)
            {
                reason = $"Kando executable could not be validated. Executable={executablePath}. {ex.Message}";
                return false;
            }
        }

        private static ushort ReadMachine(string executablePath)
        {
            using var stream = File.OpenRead(executablePath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
                throw new BadImageFormatException("The file does not have a valid DOS header.");

            stream.Position = 0x3c;
            var peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset < 0 || peHeaderOffset > stream.Length - 6)
                throw new BadImageFormatException("The PE header offset is invalid.");

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
                throw new BadImageFormatException("The file does not have a valid PE header.");

            return reader.ReadUInt16();
        }
    }
}
