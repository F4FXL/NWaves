﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NWaves.Operations;
using NWaves.Signals;
using NWaves.Transforms;
using NWaves.Utils;

namespace NWaves.Filters.Base
{
    /// <summary>
    /// Class representing Finite Impulse Response filters
    /// </summary>
    public class FirFilter : LtiFilter
    {
        /// <summary>
        /// Filter's kernel.
        /// 
        /// Numerator part coefficients in filter's transfer function 
        /// (non-recursive part in difference equations)
        /// </summary>
        public double[] Kernel
        {
            get
            {
                return _kernel;
            }
            protected set
            {
                _kernel = value;
                _kernel32 = _kernel.ToFloats();
            }
        }
        private double[] _kernel;

        /// <summary>
        /// 
        /// </summary>
        private float[] _kernel32;

        /// <summary>
        /// If Kernel.Length exceeds this value, 
        /// the filtering code will always call Overlap-Add routine.
        /// </summary>
        public const int FilterSizeForOptimizedProcessing = 64;

        /// <summary>
        /// Parameterless constructor
        /// </summary>
        protected FirFilter()
        {
        }

        /// <summary>
        /// Constructor accepting the kernel of a filter
        /// </summary>
        /// <param name="kernel"></param>
        public FirFilter(IEnumerable<double> kernel)
        {
            Kernel = kernel.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="filteringOptions"></param>
        /// <returns></returns>
        public override DiscreteSignal ApplyTo(DiscreteSignal signal, 
                                               FilteringOptions filteringOptions = FilteringOptions.Auto)
        {
            if (Kernel.Length >= FilterSizeForOptimizedProcessing && filteringOptions == FilteringOptions.Auto)
            {
                filteringOptions = FilteringOptions.OverlapAdd;
            }

            switch (filteringOptions)
            {
                case FilteringOptions.Custom:
                {
                    return ApplyFilterCircularBuffer(signal);
                }
                case FilteringOptions.OverlapAdd:
                {
                    var fftSize = MathUtils.NextPowerOfTwo(4 * Kernel.Length);
                    return Operation.OverlapAdd(signal, new DiscreteSignal(signal.SamplingRate, _kernel32), fftSize);
                }
                case FilteringOptions.OverlapSave:
                {
                    var fftSize = MathUtils.NextPowerOfTwo(4 * Kernel.Length);
                    return Operation.OverlapSave(signal, new DiscreteSignal(signal.SamplingRate, _kernel32), fftSize);
                }
                default:
                {
                    return ApplyFilterDirectly(signal);
                }
            }
        }

        /// <summary>
        /// The most straightforward implementation of the difference equation:
        /// code the difference equation as it is
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>
        public DiscreteSignal ApplyFilterDirectly(DiscreteSignal signal)
        {
            var input = signal.Samples;

            var samples = new float[input.Length];

            for (var n = 0; n < input.Length; n++)
            {
                for (var k = 0; k < _kernel32.Length; k++)
                {
                    if (n >= k) samples[n] += _kernel32[k] * input[n - k];
                }
            }

            return new DiscreteSignal(signal.SamplingRate, samples);
        }

        /// <summary>
        /// More efficient implementation of filtering in time domain:
        /// use circular buffers for recursive and non-recursive delay lines.
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>        
        public DiscreteSignal ApplyFilterCircularBuffer(DiscreteSignal signal)
        {
            var input = signal.Samples;
            
            var samples = new float[input.Length];

            // buffer for delay lines:
            var wb = new float[_kernel32.Length];
            
            var wbpos = wb.Length - 1;
            
            for (var n = 0; n < input.Length; n++)
            {
                wb[wbpos] = input[n];

                var pos = 0;
                for (var k = wbpos; k < _kernel32.Length; k++)
                {
                    samples[n] += _kernel32[pos++] * wb[k];
                }
                for (var k = 0; k < wbpos; k++)
                {
                    samples[n] += _kernel32[pos++] * wb[k];
                }

                wbpos--;
                if (wbpos < 0) wbpos = wb.Length - 1;
            }

            return new DiscreteSignal(signal.SamplingRate, samples);
        }

        /// <summary>
        /// Frequency response of an FIR filter is the FT of its impulse response
        /// </summary>
        public override ComplexDiscreteSignal FrequencyResponse(int length = 512)
        {
            var real = Kernel.PadZeros(length);
            var imag = new double[length];

            var fft = new Fft64(length);
            fft.Direct(real, imag);

            return new ComplexDiscreteSignal(1, real.Take(length / 2 + 1),
                                                imag.Take(length / 2 + 1));
        }

        /// <summary>
        /// Impulse response of an FIR filter is its kernel
        /// </summary>
        public override double[] ImpulseResponse(int length = 512)
        {
            return Kernel;
        } 

        /// <summary>
        /// Zeros of the transfer function
        /// </summary>
        public override Complex[] Zeros
        {
            get { return TransferFunction.TfToZp(Kernel); }
            set { Kernel = TransferFunction.ZpToTf(value); }
        }

        /// <summary>
        /// Poles of the transfer function (FIR filter does not have poles)
        /// </summary>
        public override Complex[] Poles
        {
            get { return null; }
            set { }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IirFilter AsIir()
        {
            return new IirFilter(Kernel, new []{ 1.0 });
        }

        /// <summary>
        /// Load kernel from csv file
        /// </summary>
        /// <param name="stream"></param>
        public static FirFilter FromCsv(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                var content = reader.ReadToEnd();
                var kernel = content.Split(';').Select(double.Parse);
                return new FirFilter(kernel);
            }
        }

        /// <summary>
        /// Serialize kernel to csv file
        /// </summary>
        /// <param name="stream"></param>
        public void Serialize(Stream stream)
        {
            using (var writer = new StreamWriter(stream))
            {
                var content = string.Join(";", Kernel.Select(k => k.ToString()));
                writer.WriteLine(content);
            }
        }

        /// <summary>
        /// Sequential combination of two FIR filters
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static FirFilter operator *(FirFilter filter1, FirFilter filter2)
        {
            var kernel1 = new DiscreteSignal(1, filter1._kernel32);
            var kernel2 = new DiscreteSignal(1, filter2._kernel32);
            var kernel = Operation.Convolve(kernel1, kernel2);

            return new FirFilter(kernel.Samples.ToDoubles());
        }

        /// <summary>
        /// Sequential combination of a FIR and an IIR filters
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static IirFilter operator *(FirFilter filter1, IirFilter filter2)
        {
            return filter1.AsIir() * filter2;
        }
    }
}
