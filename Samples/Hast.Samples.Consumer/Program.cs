﻿using Hast.Algorithms;
using Hast.Communication.Exceptions;
using Hast.Layer;
using Hast.Samples.Consumer.SampleRunners;
using Hast.Samples.SampleAssembly;
using Hast.Transformer.Vhdl.Abstractions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hast.Samples.Consumer
{
    // In this simple console application we generate hardware from some sample algorithms. Note that the project also
    // references other projects (and the sample assembly as well), so check out those too on hints which Hastlayer
    // projects to reference from your own projects.

    // Configure the whole sample project here:
    internal static class Configuration
    {
        /// <summary>
        /// Specify a path here where the VHDL file describing the hardware to be generated will be saved. If the path
        /// is relative (like the default) then the file will be saved along this project's executable in the bin output
        /// directory. If an empty string or null is specified then no file will be generated.
        /// </summary>
        public static string VhdlOutputFilePath = @"Hast_IP.vhd";

        /// <summary>
        /// Which sample algorithm to transform and run? Choose one. Currently the GenomeMatcher sample is not up-to-date
        /// enough and shouldn't be really taken as good examples (check out the other ones).
        /// </summary>
        public static Sample SampleToRun = Sample.PrimeCalculator;
    }


    class Program
    {
        static async Task MainTask(string[] args)
        {
            /*
            * On a high level these are the steps to use Hastlayer:
            * 1. Create the Hastlayer shell.
            * 2. Configure hardware generation and generate FPGA hardware representation of the given .NET code.
            * 3. Generate proxies for hardware-transformed types and use these proxies to utilize hardware
            *    implementations. (You can see this inside the SampleRunners.)
            */

            // Configuring the Hastlayer shell. Which flavor should we use? If you're unsure then you'll need
            // the Client flavor: This will let you connect to a remote Hastlayer service to run the software
            // to hardware transformation.
            var hastlayerConfiguration = new HastlayerConfiguration { Flavor = HastlayerFlavor.Developer };

            // Initializing a Hastlayer shell. Since this is non-trivial to do you can cache this shell object 
            // while the program runs and re-use it continuously. No need to always wrap it into a using() like 
            // here, just make sure to Dispose() it before the program terminates.
            using (var hastlayer = await Hastlayer.Create(hastlayerConfiguration))
            {
                // Hooking into an event of Hastlayer so some execution information can be made visible on the
                // console.
                hastlayer.ExecutedOnHardware += (sender, e) =>
                {
                    Console.WriteLine(
                        "Executing " +
                        e.MemberFullName +
                        " on hardware took " +
                        e.HardwareExecutionInformation.HardwareExecutionTimeMilliseconds +
                        " milliseconds (net) " +
                        e.HardwareExecutionInformation.FullExecutionTimeMilliseconds +
                        " milliseconds (all together)");
                };


                // We need to set what kind of device (FPGA/FPGA board) to generate the hardware for.
                var devices = await hastlayer.GetSupportedDevices();
                if (devices == null || devices.Count() == 0) throw new Exception("No devices are available!");

                // Let's just use the first one that is available, unless the user specified the -d command line flag.
                var targetDeviceName = args.Contains("-d") ? args[(args as IList<string>).IndexOf("-d") + 1] : devices.First().Name;
                var selectedDevice = devices.FirstOrDefault(device => device.Name == targetDeviceName);
                if (selectedDevice == null) throw new Exception($"Target device '{targetDeviceName}' not found!");

                var configuration = new HardwareGenerationConfiguration(selectedDevice.Name);

                // If you're running Hastlayer in the Client flavor, you also need to configure some credentials here:
                var remoteClientConfiguration = configuration.RemoteClientConfiguration();
                remoteClientConfiguration.AppName = "TestApp";
                remoteClientConfiguration.AppSecret = "appsecret";
                if (hastlayerConfiguration.Flavor == HastlayerFlavor.Client &&
                    remoteClientConfiguration.AppSecret == "appsecret")
                {
                    throw new InvalidOperationException(
                        "You haven't changed the default remote credentials! Write to crew@hastlayer.com to receive access if you don't have yet.");
                }

                if (args.Contains("-s"))
                    Configuration.SampleToRun = (Sample)Enum.Parse(typeof(Sample), args[(args as IList<string>).IndexOf("-s") + 1], true);

                // Letting the configuration of samples run. Check out those methods too!
                switch (Configuration.SampleToRun)
                {
                    case Sample.Fix64Calculator:
                        Fix64CalculatorSampleRunner.Configure(configuration);
                        break;
                    case Sample.GenomeMatcher:
                        GenomeMatcherSampleRunner.Configure(configuration);
                        break;
                    case Sample.ParallelAlgorithm:
                        ParallelAlgorithmSampleRunner.Configure(configuration);
                        break;
                    case Sample.ImageProcessingAlgorithms:
                        ImageProcessingAlgorithmsSampleRunner.Configure(configuration);
                        break;
                    case Sample.Loopback:
                        LoopbackSampleRunner.Configure(configuration);
                        break;
                    case Sample.MemoryTest:
                        MemoryTestSampleRunner.Configure(configuration);
                        break;
                    case Sample.MonteCarloPiEstimator:
                        MonteCarloPiEstimatorSampleRunner.Configure(configuration);
                        break;
                    case Sample.ObjectOrientedShowcase:
                        ObjectOrientedShowcaseSampleRunner.Configure(configuration);
                        break;
                    case Sample.PrimeCalculator:
                        PrimeCalculatorSampleRunner.Configure(configuration);
                        break;
                    case Sample.RecursiveAlgorithms:
                        RecursiveAlgorithmsSampleRunner.Configure(configuration);
                        break;
                    case Sample.SimdCalculator:
                        SimdCalculatorSampleRunner.Configure(configuration);
                        break;
                    default:
                        break;
                }

                // The generated VHDL code will contain debug-level information, though it will be slower to create.
                configuration.VhdlTransformerConfiguration().VhdlGenerationConfiguration = VhdlGenerationConfiguration.Debug;

                Console.WriteLine("Hardware generation starts.");

                // Generating hardware from the sample assembly with the given configuration.
                var hardwareRepresentation = await hastlayer.GenerateHardware(
                    new[]
                    {
                                // Selecting any type from the sample assembly here just to get its Assembly object.
                                typeof(PrimeCalculator).Assembly,
                                typeof(Fix64).Assembly
                    },
                    configuration);

                Console.WriteLine("Hardware generation finished.");
                Console.WriteLine();

                // Be sure to check out transformation warnings. Most of the time the issues noticed shouldn't cause 
                // any problems, but sometimes they can.
                if (hardwareRepresentation.HardwareDescription.Warnings.Any())
                {
                    Console.WriteLine(
                        "There were the following transformation warnings, which may hint on issues that can cause the hardware implementation to produce incorrect results:" +
                        Environment.NewLine +
                        string.Join(Environment.NewLine, hardwareRepresentation.HardwareDescription.Warnings.Select(warning => "* " + warning.ToString())));
                    Console.WriteLine();
                }

                if (!string.IsNullOrEmpty(Configuration.VhdlOutputFilePath))
                {
                    Console.WriteLine("Writing VHDL source to file.");

                    await hardwareRepresentation.HardwareDescription.WriteSource(Configuration.VhdlOutputFilePath);

                    Console.WriteLine("VHDL source written to file.");
                    Console.WriteLine();
                }

                Console.WriteLine("Starting hardware execution.");

                // Running samples.
                try
                {
                    switch (Configuration.SampleToRun)
                    {
                        case Sample.Fix64Calculator:
                            await Fix64CalculatorSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.GenomeMatcher:
                            await GenomeMatcherSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.ParallelAlgorithm:
                            await ParallelAlgorithmSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.ImageProcessingAlgorithms:
                            await ImageProcessingAlgorithmsSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.Loopback:
                            await LoopbackSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.MemoryTest:
                            await MemoryTestSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.MonteCarloPiEstimator:
                            await MonteCarloPiEstimatorSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.ObjectOrientedShowcase:
                            await ObjectOrientedShowcaseSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.PrimeCalculator:
                            await PrimeCalculatorSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.RecursiveAlgorithms:
                            await RecursiveAlgorithmsSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        case Sample.SimdCalculator:
                            await SimdCalculatorSampleRunner.Run(hastlayer, hardwareRepresentation);
                            break;
                        default:
                            break;
                    }
                }
                catch (AggregateException ex) when (ex.InnerException is HardwareExecutionResultMismatchException)
                {
                    // If you set ProxyGenerationConfiguration.VerifyHardwareResults to true (when calling
                    // GenerateProxy()) then everything will be computed in software as well to check the hardware.
                    // You'll get such an exception if there is any mismatch. This shouldn't normally happen, but it's
                    // not impossible in corner cases.
                    var mismatches = ((HardwareExecutionResultMismatchException)ex.InnerException).Mismatches;
                    var mismatchCount = mismatches.Count();
                    Console.WriteLine($"There {(mismatchCount == 1 ? "was a mismatch" : $"were {mismatchCount} mismatches")} between the software and hardware execution's results! Mismatch{(mismatchCount == 1 ? string.Empty : $"es")}:");

                    foreach (var mismatch in mismatches)
                    {
                        Console.WriteLine("* " + mismatch.ToString());
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            // Wrapping the whole program into Task.Run() is a workaround for async just to be able to run all this from 
            // inside a console app.
            try
            {
                MainTask(args).Wait();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
