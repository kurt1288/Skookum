﻿// *********************************************************************************
// 
// This tuner is, for the most part, a C# rewrite of 
// the Gedas tuner (with some adaptations for use with Puffin).
// The original source code can be found here:
// https://github.com/GediminasMasaitis/texel-tuner
//
// *********************************************************************************

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;

namespace Puffin.Tuner
{
   internal partial class Tuner
   {
      const double Epsilon = 1e-7;
      const string PositionsFile = @"./lichess-big3-resolved.book";

      private class Trace
      {
         public double[][] material = new double[6][];
         public double[][] pst = new double[384][];
         public double[][] knightMobility = new double[9][];
         public double[][] bishopMobility = new double[14][];
         public double[][] rookMobility = new double[15][];
         public double[][] queenMobility = new double[28][];
         public double[][] kingAttackWeights = new double[5][];
         public double[][] pawnShield = new double[4][];
         public double score = 0;

         public Trace()
         {
            for (int i = 0; i < 6; i++)
            {
               material[i] = new double[2];
            }

            for (int i = 0; i < 384; i++)
            {
               pst[i] = new double[2];
            }

            for (int i = 0; i < 9; i++)
            {
               knightMobility[i] = new double[2];
            }

            for (int i = 0; i < 14; i++)
            {
               bishopMobility[i] = new double[2];
            }

            for (int i = 0; i < 15; i++)
            {
               rookMobility[i] = new double[2];
            }

            for (int i = 0; i < 28; i++)
            {
               queenMobility[i] = new double[2];
            }

            for (int i = 0; i < 5; i++)
            {
               kingAttackWeights[i] = new double[2];
            }

            for (int i = 0; i < 4; i++)
            {
               pawnShield[i] = new double[2];
            }
         }
      }

      private struct ParameterWeight
      {
         public double Mg;
         public double Eg;

         public ParameterWeight() { }

         public ParameterWeight(double mg, double eg)
         {
            Mg = mg;
            Eg = eg;
         }
      }

      private readonly struct CoefficientEntry
      {
         public readonly short Value;
         public readonly int Index;

         public CoefficientEntry(short value, int index)
         {
            Index = index;
            Value = value;
         }
      }

      private class Entry
      {
         public readonly List<CoefficientEntry> Coefficients;
         public readonly double Phase;
         public readonly double Result;

         public Entry(List<CoefficientEntry> coefficients, double phase, double result)
         {
            Coefficients = coefficients;
            Phase = phase;
            Result = result;
         }
      }

      // readonly Engine Engine;
      private ParameterWeight[] Parameters = new ParameterWeight[465];

      public Tuner()
      {
         Evaluation.PieceValues[(int)PieceType.Pawn] = new(100, 100);
         Evaluation.PieceValues[(int)PieceType.Knight] = new(300, 300);
         Evaluation.PieceValues[(int)PieceType.Bishop] = new(325, 325);
         Evaluation.PieceValues[(int)PieceType.Rook] = new(500, 500);
         Evaluation.PieceValues[(int)PieceType.Queen] = new(900, 900);

         for (int i = 0; i < 384; i++)
         {
            Evaluation.PST[i] = new Score();
         }

         for (int i = 0; i < 9; i++)
         {
            Evaluation.KnightMobility[i] = new Score();
         }

         for (int i = 0; i < 14; i++)
         {
            Evaluation.BishopMobility[i] = new Score();
         }

         for (int i = 0; i < 15; i++)
         {
            Evaluation.RookMobility[i] = new Score();
         }

         for (int i = 0; i < 28; i++)
         {
            Evaluation.QueenMobility[i] = new Score();
         }

         for (int i = 0; i < 5; i++)
         {
            Evaluation.KingAttackWeights[i] = new Score();
         }

         for (int i = 0; i < 4; i++)
         {
            Evaluation.PawnShield[i] = new Score();
         }
      }

      public void Run(int maxEpochs = 3000)
      {
         Console.WriteLine($"Number of epochs set to: {maxEpochs}");

         LoadParameters();
         Console.WriteLine($"Loading positions...");
         Entry[] entries = LoadPositions();

         Console.WriteLine($"\r\nCalculating K value...");
         //double K = FindK(entries);
         double K = 2.5;
         Console.WriteLine($"K value: {K}");

         // double avgError = GetAverageError(entries, K);
         // double bestError = avgError + Epsilon * 2;
         // Console.WriteLine($"Initial average error: {avgError}");

         double learningRate = 1;
         ParameterWeight[] momentum = new ParameterWeight[Parameters.Length];
         ParameterWeight[] velocity = new ParameterWeight[Parameters.Length];

         int epoch = 1;

         Console.WriteLine("Tuning...");
         Stopwatch timer = new();
         timer.Start();

         // optional condition: Math.Abs(bestError - avgError) >= Epsilon && 
         while (epoch < maxEpochs)
         {
            ParameterWeight[] gradients = ComputeGradient(entries, K);
            double beta1 = 0.9;
            double beta2 = 0.999;

            for (int parameterIndex = 0; parameterIndex < Parameters.Length; parameterIndex++)
            {
               double grad = -K / 400 * gradients[parameterIndex].Mg / entries.Length;
               momentum[parameterIndex].Mg = beta1 * momentum[parameterIndex].Mg + (1 - beta1) * grad;
               velocity[parameterIndex].Mg = beta2 * velocity[parameterIndex].Mg + (1 - beta2) * grad * grad;
               Parameters[parameterIndex].Mg -= learningRate * momentum[parameterIndex].Mg / (1e-8 + Math.Sqrt(velocity[parameterIndex].Mg));

               grad = -K / 400 * gradients[parameterIndex].Eg / entries.Length;
               momentum[parameterIndex].Eg = beta1 * momentum[parameterIndex].Eg + (1 - beta1) * grad;
               velocity[parameterIndex].Eg = beta2 * velocity[parameterIndex].Eg + (1 - beta2) * grad * grad;
               Parameters[parameterIndex].Eg -= learningRate * momentum[parameterIndex].Eg / (1e-8 + Math.Sqrt(velocity[parameterIndex].Eg));
            }

            if (epoch % 100 == 0)
            {
               // bestError = avgError;
               // avgError = GetAverageError(entries, K);
               Console.WriteLine($"Epoch: {epoch}, EPS: {1000 * (long)epoch / timer.ElapsedMilliseconds}, Time: {timer.Elapsed:hh\\:mm\\:ss}. Remaining: {TimeSpan.FromMilliseconds((maxEpochs - epoch) * (timer.ElapsedMilliseconds / epoch)):hh\\:mm\\:ss}");
            }

            epoch += 1;
         }

         PrintResults();
         Console.WriteLine("Completed");
         Environment.Exit(100);
      }

      private ParameterWeight[] ComputeGradient(Entry[] entries, double K)
      {
         ConcurrentBag<ParameterWeight[]> gradients = new();

         Parallel.For(0, entries.Length, () => new ParameterWeight[Parameters.Length],
            (j, loop, localGradients) =>
            {
               UpdateSingleGradient(entries[j], K, ref localGradients);
               return localGradients;
            }, gradients.Add);

         return gradients.SelectMany(g => g).ToArray();
      }

      private void UpdateSingleGradient(Entry entry, double K, ref ParameterWeight[] gradient)
      {
         double sig = Sigmoid(K, Evaluate(entry));
         double res = (entry.Result - sig) * sig * (1.0 - sig);

         double mg_base = res * (entry.Phase / 24);
         double eg_base = res - mg_base;

         foreach (CoefficientEntry coef in entry.Coefficients)
         {
            gradient[coef.Index].Mg += mg_base * coef.Value;
            gradient[coef.Index].Eg += eg_base * coef.Value;
         }
      }

      private double FindK(List<Entry> entries)
      {
         double rate = 10;
         double delta = 1e-5;
         double deviation_goal = 1e-6;
         double K = 2.5;
         double deviation = 1;

         while (Math.Abs(deviation) > deviation_goal)
         {
            double up = GetAverageError(entries, K + delta);
            double down = GetAverageError(entries, K - delta);
            deviation = (up - down) / (2 * delta);
            K -= deviation * rate;
         }

         return K;
      }

      private double GetAverageError(List<Entry> entries, double K)
      {
         double sum = 0;

         Parallel.For(0, entries.Count, () => 0.0,
            (j, loop, subtotal) =>
            {
               subtotal += Math.Pow(entries[j].Result - Sigmoid(K, Evaluate(entries[j])), 2);
               return subtotal;
            },
            subtotal => Add(ref sum, subtotal));

         return sum / entries.Count;
      }

      private double Evaluate(Entry entry)
      {
         double midgame = 0;
         double endgame = 0;
         double score = 0;

         foreach (CoefficientEntry coef in entry.Coefficients)
         {
            midgame += coef.Value * Parameters[coef.Index].Mg;
            endgame += coef.Value * Parameters[coef.Index].Eg;
         }

         score += (midgame * entry.Phase + endgame * (24 - entry.Phase)) / 24;

         return score;
      }

      public void LoadParameters()
      {
         int index = 0;
         AddParameters(Evaluation.PieceValues, ref index);
         AddParameters(Evaluation.PST, ref index);
         AddParameters(Evaluation.KnightMobility, ref index);
         AddParameters(Evaluation.BishopMobility, ref index);
         AddParameters(Evaluation.RookMobility, ref index);
         AddParameters(Evaluation.QueenMobility, ref index);
         AddParameters(Evaluation.KingAttackWeights, ref index);
         AddParameters(Evaluation.PawnShield, ref index);
      }

      private void AddParameters(Score[] values, ref int index)
      {
         foreach (Score value in values)
         {
            Parameters[index++] = new(value.Mg, value.Eg);
         }
      }

      private Entry[] LoadPositions()
      {
         int lines = 0;
         int totalLines = System.IO.File.ReadLines(PositionsFile).Count();
         Entry[] entries = new Entry[totalLines];
         Board board = new();
         Stopwatch sw = Stopwatch.StartNew();

         using (StreamReader sr = System.IO.File.OpenText(PositionsFile))
         {
            string line = string.Empty;
            while ((line = sr.ReadLine()) != null)
            {
               board.SetPosition(line.Split("\"")[0].Trim());

               (Trace trace, double phase) = GetEval(board);

               entries[lines] = new(GetCoefficients(trace), phase, GetEntryResult(line));

               lines++;
               Console.Write($"\rPositions loaded: {lines}/{totalLines} {100 * (long)lines / totalLines}% | {sw.Elapsed}");
               board.Reset();

               // Force garbage collection every 1 million lines. This seems to help with memory issues.
               if (lines % 1000000 == 0)
               {
                  GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                  GC.Collect();
               }
            }
         }

         sw.Stop();

         return entries;
      }

      private static double GetEntryResult(string fen)
      {
         Match match = FENResultRegex().Match(fen);

         if (match.Success)
         {
            string result = match.Groups[1].Value;

            if (result == "1.0")
            {
               return 1.0;
            }
            else if (result == "0.5")
            {
               return 0.5;
            }
            else if (result == "0.0")
            {
               return 0.0;
            }
            else
            {
               throw new Exception($"Unknown fen result: {result}");
            }
         }
         else
         {
            throw new Exception("Unable to get fen result");
         }
      }

      private static (Trace trace, double phase) GetEval(Board board)
      {
         Trace trace = new();

         ulong whiteKingZone = Attacks.KingAttacks[board.GetSquareByPiece(PieceType.King, Color.White)];
         ulong blackKingZone = Attacks.KingAttacks[board.GetSquareByPiece(PieceType.King, Color.Black)];

         Score[] kingAttacks = { new(), new() };
         int[] kingAttacksCount = { 0, 0 };

         Score white = Material(board, Color.White, ref trace);
         Score black = Material(board, Color.Black, ref trace);
         white += Knights(board, Color.White, trace, blackKingZone, ref kingAttacks, ref kingAttacksCount);
         black += Knights(board, Color.Black, trace, whiteKingZone, ref kingAttacks, ref kingAttacksCount);
         white += Bishops(board, Color.White, trace, blackKingZone, ref kingAttacks, ref kingAttacksCount);
         black += Bishops(board, Color.Black, trace, whiteKingZone, ref kingAttacks, ref kingAttacksCount);
         white += Rooks(board, Color.White, trace, blackKingZone, ref kingAttacks, ref kingAttacksCount);
         black += Rooks(board, Color.Black, trace, whiteKingZone, ref kingAttacks, ref kingAttacksCount);
         white += Queens(board, Color.White, trace, blackKingZone, ref kingAttacks, ref kingAttacksCount);
         black += Queens(board, Color.Black, trace, whiteKingZone, ref kingAttacks, ref kingAttacksCount);
         white += Kings(board, trace, Color.White, ref kingAttacks, ref kingAttacksCount);
         black += Kings(board, trace, Color.Black, ref kingAttacks, ref kingAttacksCount);

         Score total = white - black;

         trace.score = (total.Mg * board.Phase + total.Eg * (24 - board.Phase)) / 24;

         return (trace, board.Phase);
      }

      private static Score Material(Board board, Color color, ref Trace trace)
      {
         Bitboard us = new(board.ColorBB[(int)color].Value);
         Score score = new();

         while (!us.IsEmpty())
         {
            int square = us.GetLSB();
            us.ClearLSB();
            Piece piece = board.Mailbox[square];

            score += Evaluation.PieceValues[(int)piece.Type];
            trace.material[(int)piece.Type][(int)piece.Color]++;

            if (piece.Color == Color.Black)
            {
               square ^= 56;
            }

            score += Evaluation.PST[(int)piece.Type * 64 + square];
            trace.pst[(int)piece.Type * 64 + square][(int)piece.Color]++;
         }

         return score;
      }

      private static Score Knights(Board board, Color color, Trace trace, ulong kingZone, ref Score[] kingAttacks, ref int[] kingAttacksCount)
      {
         Bitboard knightsBB = new(board.PieceBB[(int)PieceType.Knight].Value & board.ColorBB[(int)color].Value);
         ulong us = board.ColorBB[(int)color].Value;
         Score score = new();
         while (!knightsBB.IsEmpty())
         {
            int square = knightsBB.GetLSB();
            knightsBB.ClearLSB();
            int attacks = new Bitboard(Attacks.KnightAttacks[square] & ~us).CountBits();
            score += Evaluation.KnightMobility[attacks];
            trace.knightMobility[attacks][(int)color]++;

            if ((Attacks.KnightAttacks[square] & kingZone) != 0)
            {
               kingAttacksCount[(int)color]++;
               kingAttacks[(int)color] += Evaluation.KingAttackWeights[(int)PieceType.Knight] * new Bitboard(Attacks.KnightAttacks[square] & kingZone).CountBits();
               trace.kingAttackWeights[(int)PieceType.Knight][(int)color] += new Bitboard(Attacks.KnightAttacks[square] & kingZone).CountBits();
            }
         }
         return score;
      }

      private static Score Bishops(Board board, Color color, Trace trace, ulong kingZone, ref Score[] kingAttacks, ref int[] kingAttacksCount)
      {
         Bitboard bishopBB = new(board.PieceBB[(int)PieceType.Bishop].Value & board.ColorBB[(int)color].Value);
         ulong us = board.ColorBB[(int)color].Value;
         ulong occupied = board.ColorBB[(int)Color.White].Value | board.ColorBB[(int)Color.Black].Value;
         Score score = new();
         while (!bishopBB.IsEmpty())
         {
            int square = bishopBB.GetLSB();
            bishopBB.ClearLSB();
            ulong moves = Attacks.GetBishopAttacks(square, occupied);
            int attacks = new Bitboard(moves & ~us).CountBits();
            score += Evaluation.BishopMobility[attacks];
            trace.bishopMobility[attacks][(int)color]++;

            if ((moves & kingZone) != 0)
            {
               kingAttacksCount[(int)color]++;
               kingAttacks[(int)color] += Evaluation.KingAttackWeights[(int)PieceType.Bishop] * new Bitboard(moves & kingZone).CountBits();
               trace.kingAttackWeights[(int)PieceType.Bishop][(int)color] += new Bitboard(moves & kingZone).CountBits();
            }
         }
         return score;
      }

      private static Score Rooks(Board board, Color color, Trace trace, ulong kingZone, ref Score[] kingAttacks, ref int[] kingAttacksCount)
      {
         Bitboard rooksBB = new(board.PieceBB[(int)PieceType.Rook].Value & board.ColorBB[(int)color].Value);
         ulong us = board.ColorBB[(int)color].Value;
         ulong occupied = board.ColorBB[(int)Color.White].Value | board.ColorBB[(int)Color.Black].Value;
         Score score = new();
         while (!rooksBB.IsEmpty())
         {
            int square = rooksBB.GetLSB();
            rooksBB.ClearLSB();
            ulong moves = Attacks.GetRookAttacks(square, occupied);
            int attacks = new Bitboard(moves & ~us).CountBits();
            score += Evaluation.RookMobility[attacks];
            trace.rookMobility[attacks][(int)color]++;

            if ((moves & kingZone) != 0)
            {
               kingAttacksCount[(int)color]++;
               kingAttacks[(int)color] += Evaluation.KingAttackWeights[(int)PieceType.Rook] * new Bitboard(moves & kingZone).CountBits();
               trace.kingAttackWeights[(int)PieceType.Rook][(int)color] += new Bitboard(moves & kingZone).CountBits();
            }
         }
         return score;
      }
      private static Score Queens(Board board, Color color, Trace trace, ulong kingZone, ref Score[] kingAttacks, ref int[] kingAttacksCount)
      {
         Bitboard queensBB = new(board.PieceBB[(int)PieceType.Queen].Value & board.ColorBB[(int)color].Value);
         ulong us = board.ColorBB[(int)color].Value;
         ulong occupied = board.ColorBB[(int)Color.White].Value | board.ColorBB[(int)Color.Black].Value;
         Score score = new();
         while (!queensBB.IsEmpty())
         {
            int square = queensBB.GetLSB();
            queensBB.ClearLSB();
            ulong moves = Attacks.GetQueenAttacks(square, occupied);
            int attacks = new Bitboard(moves & ~us).CountBits();
            score += Evaluation.QueenMobility[attacks];
            trace.queenMobility[attacks][(int)color]++;

            if ((moves & kingZone) != 0)
            {
               kingAttacksCount[(int)color]++;
               kingAttacks[(int)color] += Evaluation.KingAttackWeights[(int)PieceType.Queen] * new Bitboard(moves & kingZone).CountBits();
               trace.kingAttackWeights[(int)PieceType.Queen][(int)color] += new Bitboard(moves & kingZone).CountBits();
            }
         }
         return score;
      }

      private static Score Kings(Board board, Trace trace, Color color, ref Score[] kingAttacks, ref int[] kingAttacksCount)
      {
         Score score = new();
         Bitboard kingBB = new(board.PieceBB[(int)PieceType.King].Value & board.ColorBB[(int)color].Value);
         int kingSq = kingBB.GetLSB();
         ulong kingSquares = color == Color.White ? 0xD7C3000000000000 : 0xC3D7;

         if ((kingSquares & Constants.SquareBB[kingSq]) != 0)
         {
            ulong pawnSquares = color == Color.White ? (ulong)(kingSq % 8 < 3 ? 0x007000000000000 : 0x000E0000000000000) : (ulong)(kingSq % 8 < 3 ? 0x700 : 0xE000);

            Bitboard pawns = new(board.PieceBB[(int)PieceType.Pawn].Value & board.ColorBB[(int)color].Value & pawnSquares);
            score += Evaluation.PawnShield[Math.Min(pawns.CountBits(), 3)];
            trace.pawnShield[Math.Min(pawns.CountBits(), 3)][(int)color]++;
         }

         if (kingAttacksCount[(int)color ^ 1] >= 2)
         {
            score -= kingAttacks[(int)color ^ 1];
         }

         return score;
      }

      private List<CoefficientEntry> GetCoefficients(Trace trace)
      {
         List<CoefficientEntry> coefficients = new();

         GetCoefficientsFromArray(ref coefficients, trace.material, 6);
         GetCoefficientsFromArray(ref coefficients, trace.pst, 384);
         GetCoefficientsFromArray(ref coefficients, trace.knightMobility, 9);
         GetCoefficientsFromArray(ref coefficients, trace.bishopMobility, 14);
         GetCoefficientsFromArray(ref coefficients, trace.rookMobility, 15);
         GetCoefficientsFromArray(ref coefficients, trace.queenMobility, 28);
         GetCoefficientsFromArray(ref coefficients, trace.kingAttackWeights, 5);
         GetCoefficientsFromArray(ref coefficients, trace.pawnShield, 4);

         return coefficients;
      }

      private void GetCoefficientsFromArray(ref List<CoefficientEntry> coefficients, double[][] trace, int size)
      {
         for (int i = 0; i < size; i++)
         {
            if (trace[i][0] - trace[i][1] != 0)
            {
               coefficients.Add(new((short)(trace[i][0] - trace[i][1]), i));
            }
         }
      }

      private double Sigmoid(double factor, double score)
      {
         return 1.0 / (1.0 + Math.Exp(-(factor * score)));
      }

      // From https://stackoverflow.com/a/16893641
      private static double Add(ref double location1, double value)
      {
         double newCurrentValue = location1;
         while (true)
         {
            double currentValue = newCurrentValue;
            double newValue = currentValue + value;
            newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
            if (newCurrentValue.Equals(currentValue))
               return newValue;
         }
      }

      private void PrintResults()
      {
         string path = @$"./Tuning_Results_{DateTime.Now.ToString("yyyy-MM-dd,HHmmss")}.txt";

         if (!System.IO.File.Exists(path))
         {
            string createText = $"Tuning results generated on {DateTime.Now.ToString("yyyy-MM-dd,HHmmss")}\r\n";
            System.IO.File.WriteAllText(path, createText);
         }

         using StreamWriter sw = new(path, true);

         int index = 0;
         PrintArray("material", ref index, 6, sw);
         PrintPSTArray("pst", ref index, sw);
         PrintArray("knight mobility", ref index, 9, sw);
         PrintArray("bishop mobility", ref index, 14, sw);
         PrintArray("rook mobility", ref index, 15, sw);
         PrintArray("queen mobility", ref index, 28, sw);
         PrintArray("king attack weights", ref index, 5, sw);
         PrintArray("pawn shield", ref index, 4, sw);
      }

      private void PrintArray(string name, ref int index, int count, StreamWriter writer)
      {
         int start = index;
         writer.WriteLine(name);
         for (int i = start; i < start + count; i++)
         {
            index += 1;
            string values = $"new({(int)Parameters[i].Mg}, {(int)Parameters[i].Eg}),";
            writer.WriteLine(values);
         }
         writer.WriteLine("\r\n");
      }

      private void PrintPSTArray(string name, ref int index, StreamWriter writer)
      {
         int offset = index;
         StringBuilder stringBuilder = new();
         writer.WriteLine(name);

         for (int piece = 0; piece < 6; ++piece)
         {
            for (int square = 0; square < 64; ++square)
            {
               int i = piece * 64 + square + offset;
               stringBuilder.Append($"new({(int)Parameters[i].Mg,3}, {(int)Parameters[i].Eg,3}), ");
               index += 1;

               if (square % 8 == 7)
               {
                  writer.WriteLine(stringBuilder);
                  stringBuilder.Clear();
               }
            }

            writer.WriteLine();
         }
      }

      [GeneratedRegex("\\[([^]]+)\\]")]
      private static partial Regex FENResultRegex();
   }
}
