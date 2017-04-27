﻿using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveStacks
{
    class Program
    {
        private static StackResolver _resolver;
        private static LiveSession _session;
        private static Options _options;
        private static Timer _timer;
        private static int _invocationsLeft;

        private static void Main(string[] args)
        {
            var parser = new Parser(ps =>
            {
                ps.CaseSensitive = true;
                ps.IgnoreUnknownArguments = false;
            });
            _options = new Options();
            if (!parser.ParseArguments(args, _options))
                Environment.Exit(1);

            _invocationsLeft = _options.Count == -1 ? int.MaxValue : _options.Count;

            try
            {
                _session = new LiveSession(_options.StackEvent, _options.PidsToFilter, _options.IncludeKernelFrames);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error creating session: " + ex.Message);
                Environment.Exit(1);
            }
            Console.CancelKeyPress += (_, __) =>
            {
                Console.WriteLine("Ctrl+C pressed, stopping...");
                _session.Stop();
            };

            _resolver = new StackResolver();
            object timerSyncObject = new object();
            TimeSpan interval = TimeSpan.FromSeconds(_options.IntervalSeconds);
            _timer = new Timer(_ =>
            {
                // Prevent multiple invocations of the timer from running concurrently.
                lock (timerSyncObject)
                {
                    OnTimer();
                    if (--_invocationsLeft == 0)
                    {
                        _session.Stop();
                    }
                }
            }, null, interval, interval);

            _session.Start();
        }

        private static void OnTimer()
        {
            if (_options.ClearScreen)
            {
                Console.Clear();
            }
            if (!_options.FoldedStacks)
            {
                Console.WriteLine(DateTime.Now.ToLongTimeString());
            }
            Stopwatch sw = Stopwatch.StartNew();
            var stacks = _session.Stacks.TopStacks(_options.TopStacks);
            _session.Stacks.Clear();
            foreach (var stack in stacks)
            {
                if (_options.FoldedStacks)
                    PrintFoldedStack(stack);
                else
                    PrintNormalStack(stack);
            }
            if (!_options.FoldedStacks)
            {
                Console.WriteLine($"  Time aggregating/resolving: {sw.ElapsedMilliseconds}ms");
            }
        }

        private static void PrintNormalStack(AggregatedStack stack)
        {
            int pid = stack.ProcessID;
            Console.WriteLine($"  {stack.Count,10} [{_resolver.ProcessName(pid)} {pid}]");
            foreach (var symbol in _resolver.Resolve(pid, stack.Addresses))
            {
                Console.WriteLine("    " + symbol.ToString());
            }
            Console.WriteLine();
        }

        private static void PrintFoldedStack(AggregatedStack stack)
        {
            int pid = stack.ProcessID;
            Console.Write($"{_resolver.ProcessName(pid)} ({pid})");
            foreach (var symbol in _resolver.Resolve(pid, stack.Addresses).Reverse())
            {
                Console.Write(";" + symbol.ToString());
            }
            Console.WriteLine(" " + stack.Count);
        }
    }
}
