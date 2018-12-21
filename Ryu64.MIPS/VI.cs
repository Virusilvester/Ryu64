﻿using System.Threading;

namespace Ryu64.MIPS
{
    public class VI
    {
        private static uint CurrentScanline = 0;

        public static void Start()
        {
            Thread VIThread = new Thread(() =>
            {
                while (R4300.R4300_ON)
                {
                    R4300.memory.WriteScanline(CurrentScanline);
                    MI.PollVIInterrupt(CurrentScanline);
                    ++CurrentScanline;
                    if (CurrentScanline == 525)
                        CurrentScanline = 0;
                    Thread.Sleep(2);
                }
            })
            {
                Name = "VIThread"
            };
            VIThread.Start();
        }
    }
}