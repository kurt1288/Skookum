﻿using static Puffin.Constants;

namespace Puffin
{
   internal class Engine
   {
      private readonly Board Board = new();
      private readonly TimeManager Timer = new();
      private TranspositionTable TTable = new();
      private int Threads = 1;
      private ThreadManager SearchManager;

      public Engine()
      {
         // Initializes the Attacks static class
         System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Attacks.Attacks).TypeHandle);

         // Initializes the Constants static class
         System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Constants).TypeHandle);

         // Initializes the Zobirst table static class
         System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Zobrist).TypeHandle);

         SearchManager = new(Threads, ref TTable);
      }

      public void NewGame()
      {
         Board.Reset();
         TTable.Reset();
         Timer.Reset();
         SearchManager.Reset();
      }

      public void SetPosition(string fen)
      {
         Board.SetPosition(fen);
      }

      public void MakeMoves(string[] moves)
      {
         foreach (string move in moves)
         {
            MoveFlag flag = MoveFlag.Quiet;
            int from = (byte)Enum.Parse(typeof(Square), move.Substring(0, 2).ToUpper());
            int to = (byte)Enum.Parse(typeof(Square), move.Substring(2, 2).ToUpper());
            Piece piece = Board.Squares[from];

            if (piece.Type == PieceType.Pawn && Math.Abs((from >> 3) - (to >> 3)) == 2)
            {
               flag = MoveFlag.DoublePawnPush;
            }

            if (Board.Squares[to].Type != PieceType.Null)
            {
               flag = MoveFlag.Capture;
            }

            if (to == (int)Board.EnPassant && piece.Type == PieceType.Pawn)
            {
               flag = MoveFlag.EPCapture;
            }

            if (piece.Type == PieceType.King)
            {
               if (move == "e1g1" || move == "e8g8")
               {
                  flag = MoveFlag.KingCastle;
               }
               else if (move == "e1c1" || move == "e8c8")
               {
                  flag = MoveFlag.QueenCastle;
               }
            }

            if (move.Length == 5)
            {
               if (char.ToLower(move[4]) == 'n')
               {
                  if (Board.Squares[to].Type != PieceType.Null)
                  {
                     flag = MoveFlag.KnightPromotionCapture;
                  }
                  else
                  {
                     flag = MoveFlag.KnightPromotion;
                  }
               }
               else if (char.ToLower(move[4]) == 'b')
               {
                  if (Board.Squares[to].Type != PieceType.Null)
                  {
                     flag = MoveFlag.BishopPromotionCapture;
                  }
                  else
                  {
                     flag = MoveFlag.BishopPromotion;
                  }
               }
               else if (char.ToLower(move[4]) == 'r')
               {
                  if (Board.Squares[to].Type != PieceType.Null)
                  {
                     flag = MoveFlag.RookPromotionCapture;
                  }
                  else
                  {
                     flag = MoveFlag.RookPromotion;
                  }
               }
               else if (char.ToLower(move[4]) == 'q')
               {
                  if (Board.Squares[to].Type != PieceType.Null)
                  {
                     flag = MoveFlag.QueenPromotionCapture;
                  }
                  else
                  {
                     flag = MoveFlag.QueenPromotion;
                  }
               }
            }

            Board.MakeMove(new Move(from, to, flag));
         }
      }

      public ulong Perft(int depth)
      {
         Perft perft = new(Board);
         return perft.Run(depth);
      }

      public int Evaluate()
      {
         return Evaluation.Evaluate(Board);
      }

      public void UCIParseGo(string[] command)
      {
         Timer.Reset();
         int wtime = 0;
         int btime = 0;
         int winc = 0;
         int binc = 0;
         int movestogo = 0;
         int movetime = 0;
         int depth = 0;
         int nodes = 0;

         for (int i = 0; i < command.Length; i += 2)
         {
            string type = command[i];

            switch (type)
            {
               case "wtime":
                  {
                     wtime = int.Parse(command[i + 1]);
                     break;
                  }
               case "btime":
                  {
                     btime = int.Parse(command[i + 1]);
                     break;
                  }
               case "winc":
                  {
                     winc = int.Parse(command[i + 1]);
                     break;
                  }
               case "binc":
                  {
                     binc = int.Parse(command[i + 1]);
                     break;
                  }
               case "movestogo":
                  {
                     movestogo = int.Parse(command[i + 1]);
                     break;
                  }
               case "movetime":
                  {
                     movetime = int.Parse(command[i + 1]);
                     break;
                  }
               case "depth":
                  {
                     depth = Math.Clamp(int.Parse(command[1]), 1, MAX_PLY - 1);
                     break;
                  }
               case "nodes":
                  {
                     nodes = int.Parse(command[1]);
                     break;
                  }
            }
         }

         Timer.SetLimits(Board.SideToMove == Color.White ? wtime : btime, Board.SideToMove == Color.White ? winc : binc, movestogo, movetime, depth, nodes);

         SearchManager.StartSearches(Timer, Board);
      }

      public static void Bench(int depth)
      {
         Bench bench = new(depth);
         bench.Run();
      }

      public void StopSearch()
      {
         Timer.Stop();
      }

      public void SetOption(string[] option)
      {
         _ = int.TryParse(option[3], out int value);

         switch (option[1].ToLower())
         {
            case "hash":
               {
                  value = Math.Clamp(value, 1, 512);
                  TTable.Resize(value);
                  break;
               }
            case "threads":
               {
                  Threads = value;
                  SearchManager.Shutdown();
                  SearchManager = new(value, ref TTable);
                  break;
               }
            case "ASP_Min_Depth":
               {
                  Search.ASP_Min_Depth = value;
                  break;
               }
            case "ASP_Margin":
               {
                  Search.ASP_Margin = value;
                  break;
               }
            case "NMP_Min_Depth":
               {
                  Search.NMP_Min_Depth = value;
                  break;
               }
            case "RFP_Max_Depth":
               {
                  Search.RFP_Max_Depth = value;
                  break;
               }
            case "RFP_Margin":
               {
                  Search.RFP_Margin = value;
                  break;
               }
            case "LMR_Min_Depth":
               {
                  Search.LMR_Min_Depth = value;
                  break;
               }
            case "LMR_Min_MoveLimit":
               {
                  Search.LMR_Min_MoveLimit = value;
                  break;
               }
            case "FP_Max_Depth":
               {
                  Search.FP_Max_Depth = value;
                  break;
               }
            case "FP_Margin":
               {
                  Search.FP_Margin = value;
                  break;
               }
            case "LMP_Max_Depth":
               {
                  Search.LMP_Max_Depth = value;
                  break;
               }
            case "LMP_Min_Margin":
               {
                  Search.LMP_Min_Margin = value;
                  break;
               }
            case "IIR_Min_Depth":
               {
                  Search.IIR_Min_Depth = value;
                  break;
               }
            case "LMR_Quiet_Reduction_Base":
               {
                  Search.LMR_Quiet_Reduction_Base = value / 100;
                  GenerateLMReductionTable();
                  break;
               }
            case "LMR_Quiet_Reduction_Multiplier":
               {
                  Search.LMR_Quiet_Reduction_Multiplier = value / 100;
                  GenerateLMReductionTable();
                  break;
               }
            default:
               {
                  Console.WriteLine($"Unknown or unsupported option: {option[1]}");
                  break;
               }
         }
      }
   }
}
