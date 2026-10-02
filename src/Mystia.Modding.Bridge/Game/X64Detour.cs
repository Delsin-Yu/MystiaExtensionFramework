using System.Runtime.InteropServices;
using Iced.Intel;
using Il2CppInterop.Runtime.Injection;

namespace Mystia.Modding.Bridge;

/// <summary>
/// x64 detours that do not go through MonoMod. MonoMod.Core 1.3.6 installs a JIT hook
/// that throws on .NET 10 (ILGeneratorProxy constraint), so ClassInjector cannot use it.
/// </summary>
internal sealed class X64DetourProvider : IDetourProvider
{
    public IDetour Create<TDelegate>(nint original, TDelegate target) where TDelegate : Delegate =>
        new Hook(original, target);

    internal sealed class Hook : IDetour
    {
        private const int JumpSize = 14;
        private readonly byte[] _stolen;
        private readonly Delegate _target;
        private readonly nint _trampoline;
        private readonly int _stolenLength;
        private Delegate? _trampolineDelegate;
        private bool _applied;

        public Hook(nint original, Delegate target)
        {
            if (original == 0)
                throw new ArgumentException("The detour target address is zero.", nameof(original));
            _target = target;
            Target = original;
            Detour = Marshal.GetFunctionPointerForDelegate(_target);
            _stolenLength = Measure(original, JumpSize);
            _stolen = new byte[_stolenLength];
            Marshal.Copy(original, _stolen, 0, _stolenLength);
            _trampoline = AllocateNear(original, 256);
            WriteTrampoline(_trampoline, original, _stolen, original + _stolenLength);
        }

        public nint Target { get; }

        public nint Detour { get; }

        public nint OriginalTrampoline => _trampoline;

        public void Apply()
        {
            if (_applied)
                return;
            var patch = new byte[_stolenLength];
            WriteAbsoluteJump(patch, 0, Detour);
            for (var index = JumpSize; index < patch.Length; index++)
                patch[index] = 0x90;
            ProtectWrite(Target, patch);
            _applied = true;
        }

        public T GenerateTrampoline<T>() where T : Delegate
        {
            var trampoline = Marshal.GetDelegateForFunctionPointer<T>(_trampoline);
            _trampolineDelegate = trampoline;
            return trampoline;
        }

        public void Dispose()
        {
            if (_applied)
                ProtectWrite(Target, _stolen);
            _applied = false;
            if (_trampoline != 0)
                VirtualFree(_trampoline, 0, 0x8000);
        }

        private static int Measure(nint address, int minimum)
        {
            var buffer = new byte[minimum + 15];
            Marshal.Copy(address, buffer, 0, buffer.Length);
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(buffer));
            decoder.IP = unchecked((ulong)address);
            var total = 0;
            while (total < minimum)
            {
                var instruction = decoder.Decode();
                if (instruction.IsInvalid || instruction.Length == 0)
                    throw new InvalidOperationException($"Could not decode the detour site at 0x{address:X}.");
                total += instruction.Length;
            }
            return total;
        }

        private static void WriteTrampoline(nint trampoline, nint source, byte[] stolen, nint resume)
        {
            var instructions = new List<Instruction>();
            var decoder = Decoder.Create(64, new ByteArrayCodeReader(stolen));
            decoder.IP = unchecked((ulong)source);
            var consumed = 0;
            while (consumed < stolen.Length)
            {
                var instruction = decoder.Decode();
                if (instruction.IsInvalid || instruction.Length == 0)
                    throw new InvalidOperationException($"Could not decode stolen bytes at 0x{source:X}.");
                instructions.Add(instruction);
                consumed += instruction.Length;
            }

            var writer = new ByteListWriter();
            var block = new InstructionBlock(writer, instructions, unchecked((ulong)trampoline));
            if (!BlockEncoder.TryEncode(64, block, out var error, out _))
                throw new InvalidOperationException("Could not relocate the trampoline: " + error);
            var body = writer.Bytes;
            var image = new byte[body.Count + JumpSize];
            body.CopyTo(image);
            WriteAbsoluteJump(image, body.Count, resume);
            Marshal.Copy(image, 0, trampoline, image.Length);
            Flush(trampoline, image.Length);
        }

        private static void WriteAbsoluteJump(byte[] destination, int offset, nint target)
        {
            destination[offset] = 0xFF;
            destination[offset + 1] = 0x25;
            destination[offset + 2] = 0;
            destination[offset + 3] = 0;
            destination[offset + 4] = 0;
            destination[offset + 5] = 0;
            BitConverter.TryWriteBytes(destination.AsSpan(offset + 6), target.ToInt64());
        }

        private static void ProtectWrite(nint address, byte[] bytes)
        {
            if (!VirtualProtect(address, (nuint)bytes.Length, 0x40, out var previous))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(bytes, 0, address, bytes.Length);
            VirtualProtect(address, (nuint)bytes.Length, previous, out _);
            Flush(address, bytes.Length);
        }

        private static void Flush(nint address, int length)
        {
            FlushInstructionCache(GetCurrentProcess(), address, (nuint)length);
        }

        private static nint AllocateNear(nint target, int size)
        {
            var origin = target.ToInt64() & ~0xFFFFL;
            for (long distance = 0x10000; distance < 0x40000000L; distance += 0x10000)
            {
                foreach (var candidate in new[] { origin - distance, origin + distance })
                {
                    if (candidate < 0x10000)
                        continue;
                    var memory = VirtualAlloc((nint)candidate, (nuint)size, 0x1000 | 0x2000, 0x40);
                    if (memory != 0)
                        return memory;
                }
            }

            throw new InvalidOperationException($"Could not allocate a trampoline within 1 GB of 0x{target:X}.");
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protect);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool VirtualFree(nint address, nuint size, uint freeType);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32")]
        private static extern nint GetCurrentProcess();

        [DllImport("kernel32")]
        private static extern bool FlushInstructionCache(nint process, nint address, nuint size);

        private sealed class ByteListWriter : CodeWriter
        {
            public List<byte> Bytes { get; } = [];

            public override void WriteByte(byte value) => Bytes.Add(value);
        }
    }
}
