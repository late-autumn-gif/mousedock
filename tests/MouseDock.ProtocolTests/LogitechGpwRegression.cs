using MouseDock.Services.Detection;

internal static class LogitechGpwRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(4186, false, out int level) && level == 100, "GPW official full discharge trace");
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(3479, false, out level) && level == 0, "GPW official exhausted discharge trace");
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(3800, false, out level) && level == 47, "GPW energy curve 3.8V approximates47%, not generic Li-ion mapping");
        // Real 1.3.9 feedback read 16% from 0x1001: it took the high voltage byte 0x10.
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(4096, false, out level) && level == 93, "GPW 4.096V is ~93%, not the old byte-as-percent 16");
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(3700, false, out level) && level == 16, "GPW steep lower discharge curve");
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(4118, true, out level) && level == 50, "Official charging curve has distinct50% anchor");
        Check(LogitechGpwBattery.TryEstimateGpwPercentage(4197, true, out level) && level == 67, "Official pre-plateau charging anchor");
        Check(!LogitechGpwBattery.TryEstimateGpwPercentage(4200, true, out level) && level == -1, "4.2V represents68..100%, cannot uniquely recoverSoC");
        Check(!LogitechGpwBattery.TryEstimateGpwPercentage(4300, true, out _), "No charging plateau extrapolation");
        Check(!LogitechGpwBattery.TryEstimateGpwPercentage(1000, false, out _), "Reject invalid voltage");
        int previous = -1;
        for (int mv = 3479; mv <= 4186; mv++)
        {
            Check(LogitechGpwBattery.TryEstimateGpwPercentage(mv, false, out level) && level >= previous && level is >= 0 and <= 100,
                "Official discharge model must remain monotonic at " + mv);
            previous = level;
        }
        return checks;
    }
}
