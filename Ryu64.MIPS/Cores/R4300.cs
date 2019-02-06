﻿using System;
using System.IO;
using System.Threading;

namespace Ryu64.MIPS.Cores
{
    public class R4300
    {
        public static bool R4300_ON = false;

        public static Memory memory;
        public static ulong CycleCounter = 0;
        private static ulong Count = 0;

        private static uint CRC32(uint StartAddress, uint Length)
        {
            uint[] Table = new uint[256];
            ulong n, k;
            uint c;

            for (n = 0; n < 256; ++n)
            {
                c = (uint)n;

                for (k = 0; k < 8; ++k)
                {
                    if ((c & 1) == 1)
                        c = 0xEDB88320 ^ (c >> 1);
                    else
                        c >>= 1;
                }

                Table[n] = c;
            }

            c = 0 ^ 0xFFFFFFFF;

            for (n = 0; n < Length; ++n)
            {
                c = Table[(c ^ memory.ReadUInt8(StartAddress + (uint)n)) & 0xFF] ^ (c >> 8);
            }

            return c ^ 0xFFFFFFFF;
        }

        // All values from Cen64: https://github.com/tj90241/cen64/blob/master/si/cic.c
        private const uint CIC_SEED_NUS_5101 = 0x0000AC00;
        private const uint CIC_SEED_NUS_6101 = 0x00043F3F;
        private const uint CIC_SEED_NUS_6102 = 0x00003F3F;
        private const uint CIC_SEED_NUS_6103 = 0x0000783F;
        private const uint CIC_SEED_NUS_6105 = 0x0000913F;
        private const uint CIC_SEED_NUS_6106 = 0x0000853F;
        private const uint CIC_SEED_NUS_8303 = 0x0000DD00;

        private const uint CRC_NUS_5101 = 0x587BD543;
        private const uint CRC_NUS_6101 = 0x6170A4A1;
        private const uint CRC_NUS_7102 = 0x009E9EA3;
        private const uint CRC_NUS_6102 = 0x90BB6CB5;
        private const uint CRC_NUS_6103 = 0x0B050EE0;
        private const uint CRC_NUS_6105 = 0x98BC2C86;
        private const uint CRC_NUS_6106 = 0xACC8580A;
        private const uint CRC_NUS_8303 = 0x0E018159;
        private const uint CRC_iQue_1   = 0xCD19FEF1;
        private const uint CRC_iQue_2   = 0xB98CED9A;
        private const uint CRC_iQue_3   = 0xE71C2766;


        private static uint GetCICSeed()
        {
            uint CRC        = CRC32(0x10000040, 0xFC0);
            uint Aleck64CRC = CRC32(0x10000040, 0xBC0);

            if (Aleck64CRC == CRC_NUS_5101) return CIC_SEED_NUS_5101;
            switch (CRC)
            {
                default:
                    Common.Logger.PrintWarningLine("Unknown CIC, defaulting to seed CIC-6101.");
                    return CIC_SEED_NUS_6101;

                case CRC_NUS_6101:
                case CRC_NUS_7102:
                case CRC_iQue_1:
                case CRC_iQue_2:
                case CRC_iQue_3:
                    return CIC_SEED_NUS_6101;

                case CRC_NUS_6102:
                    return CIC_SEED_NUS_6102;

                case CRC_NUS_6103:
                    return CIC_SEED_NUS_6103;

                case CRC_NUS_6105:
                    return CIC_SEED_NUS_6105;

                case CRC_NUS_6106:
                    return CIC_SEED_NUS_6106;

                case CRC_NUS_8303:
                    return CIC_SEED_NUS_8303;
            }
        }

        public static bool isDelaySlot = false;

        public static ulong PrevWired = 0;

        public static void InterpretOpcode(uint Opcode, bool DelaySlot = false)
        {
            isDelaySlot = DelaySlot;

            ExceptionHandler.PollInt();

            if (Registers.R4300.Reg[0] != 0) Registers.R4300.Reg[0] = 0;

            if (Registers.COP0.Reg[Registers.COP0.COUNT_REG] >= 0xFFFFFFFF)
            {
                Registers.COP0.Reg[Registers.COP0.COUNT_REG] = 0x0;
                Count = 0x0;
            }

            OpcodeTable.OpcodeDesc Desc = new OpcodeTable.OpcodeDesc(Opcode, false, true);
            OpcodeTable.InstInfo   Info = OpcodeTable.GetOpcodeInfo (Opcode, false, true);

            PrevWired = Registers.COP0.Reg[Registers.COP0.WIRED_REG];

            Info.Interpret(Desc);
            CycleCounter += Info.Cycles;
            Count        += Info.Cycles;
            Registers.COP0.Reg[Registers.COP0.COUNT_REG] = (uint)(Count >> 1);
            --Registers.COP0.Reg[Registers.COP0.RANDOM_REG];
            if (Registers.COP0.Reg[Registers.COP0.RANDOM_REG] < Registers.COP0.Reg[Registers.COP0.WIRED_REG] 
             || Registers.COP0.Reg[Registers.COP0.RANDOM_REG] == 0
             || Registers.COP0.Reg[Registers.COP0.WIRED_REG]  != PrevWired)
                Registers.COP0.Reg[Registers.COP0.RANDOM_REG] = 0x1F;

            Common.Measure.InstructionCount += 1;
            Common.Measure.CycleCounter = CycleCounter;
        }

        public enum RomType_enum : uint
        {
            Cart = 0,
            DD = 1
        }

        public enum ResetType_enum : uint
        {
            Cold_Reset = 0,
            NMI = 1,
            Reset_To_Boot_Disk = 2
        }

        public enum TVType_enum : uint
        {
            PAL = 0,
            NTSC = 1,
            MPAL = 2
        }

        public static void PowerOnR4300(TVType_enum TVtype)
        {
            for (int i = 0; i < Registers.R4300.Reg.Length; ++i)
                Registers.R4300.Reg[i] = 0; // Clear Registers.

            uint RomType   = (uint)RomType_enum.Cart;
            uint ResetType = (uint)ResetType_enum.Cold_Reset;
            uint osVersion = 0; // 00 = 1.0, 15 = 2.5, etc.
            uint TVType    = (uint)TVtype;

            Registers.R4300.Reg[1]  = 0x0000000000000001;
            Registers.R4300.Reg[2]  = 0x000000000EBDA536;
            Registers.R4300.Reg[3]  = 0x000000000EBDA536;
            Registers.R4300.Reg[4]  = 0x000000000000A536;
            Registers.R4300.Reg[5]  = 0xFFFFFFFFC0F1D859;
            Registers.R4300.Reg[6]  = 0xFFFFFFFFA4001F0C;
            Registers.R4300.Reg[7]  = 0xFFFFFFFFA4001F08;
            Registers.R4300.Reg[8]  = 0x00000000000000C0;
            Registers.R4300.Reg[10] = 0x0000000000000040;
            Registers.R4300.Reg[11] = 0xFFFFFFFFA4000040;
            Registers.R4300.Reg[12] = 0xFFFFFFFFED10D0B3;
            Registers.R4300.Reg[13] = 0x000000001402A4CC;
            Registers.R4300.Reg[14] = 0x000000002DE108EA;
            Registers.R4300.Reg[15] = 0x000000003103E121;
            Registers.R4300.Reg[19] = RomType;
            Registers.R4300.Reg[20] = TVType;
            Registers.R4300.Reg[21] = ResetType;
            Registers.R4300.Reg[22] = (GetCICSeed() >> 8) & 0xFF;
            Registers.R4300.Reg[23] = osVersion;
            Registers.R4300.Reg[25] = 0xFFFFFFFF9DEBB54F;
            Registers.R4300.Reg[29] = 0xFFFFFFFFA4001FF0;
            Registers.R4300.Reg[31] = 0xFFFFFFFFA4001550;
            Registers.R4300.HI      = 0x000000003FC18657;
            Registers.R4300.LO      = 0x000000003103E121;
            Registers.R4300.PC      = 0xA4000040;

            memory.FastMemoryCopy(0xA4000000, 0xB0000000, 0xFFF); // Load the Boot Code into the correct memory address.

            COP0.PowerOnCOP0();
            COP1.PowerOnCOP1();

            R4300_ON = true;

            OpcodeTable.Init();

            Thread CPUThread =
            new Thread(() =>
            {
                Common.Measure.MeasureTime.Start();
                while (R4300_ON)
                {
                    uint Opcode = memory.ReadUInt32(Registers.R4300.PC);
                    InterpretOpcode(Opcode);

                    Common.Variables.CPUMHz = (Common.Measure.CycleCounter / 1000000) / Common.Measure.MeasureTime.Elapsed.TotalSeconds;
                }
                Common.Measure.MeasureTime.Stop();
            })
            {
                Name = "R4300"
            };
            CPUThread.Start();

            VI.Start();
        }
    }
}