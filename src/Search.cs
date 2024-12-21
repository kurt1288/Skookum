using System.Runtime.CompilerServices;
using System.Text;
using static Puffin.Constants;

namespace Puffin
{
   internal class Search(Board board, TimeManager time, ref TranspositionTable tTable, SearchInfo info)
   {
      // NOT THREAD-SAFE (but works well enough for now)
      private static int Nodes = 0;

      private readonly Board Board = board;
      private readonly TimeManager TimeManager = time;
      private TranspositionTable TTable = tTable;

      public SearchInfo ThreadInfo { get; } = info;

      #region SPSA Tunable Parameters
      [Tunable(min: 5, max: 20, step: 4)]
      public static int ASP_Margin { get; set; } = 10;

      [Tunable(min: 40, max: 200, step: 25)]
      public static int RFP_Margin { get; set; } = 70;

      [Tunable(min: 50, max: 200, step: 25)]
      public static int FP_Margin { get; set; } = 80;

      [Tunable(min: 0.1, max: 2.0, step: 0.5)]
      public static double LMR_Quiet_Reduction_Base { get; set; } = 1.6;

      public static double LMR_Noisy_Reduction_Base { get; set; } = 0.3;

      [Tunable(min: 0.1, max: 1.0, step: 0.5)]
      public static double LMR_Quiet_Reduction_Multiplier { get; set; } = 0.4;

      public static double LMR_Noisy_Reduction_Multiplier { get; set; } = 0.3;

      public static int SEE_Noisy_Threshold { get; set; } = -90;

      public static int SEE_Quiet_Threshold { get; set; } = -40;

      #endregion

      #region Non-SPSA Parameters (Depth and Move Thresholds)
      public static int ASP_Min_Depth { get; set; } = 4;
      public static int NMP_Min_Depth { get; set; } = 3;
      public static int RFP_Max_Depth { get; set; } = 10;
      public static int LMR_Min_Depth { get; set; } = 2;
      public static int LMR_Min_MoveLimit { get; set; } = 3;
      public static int FP_Max_Depth { get; set; } = 7;
      public static int LMP_Max_Depth { get; set; } = 8;
      public static int LMP_Min_Margin { get; set; } = 5;
      public static int IIR_Min_Depth { get; set; } = 5;
      #endregion

      private void PrintInfo(int depth)
      {
         int nodes = Nodes;
         long elapsedMs = TimeManager.GetElapsedMs();
         int score = ThreadInfo.Score;

         string scoreStr = score < -MATE + MAX_PLY ? $"mate {(-MATE - score) / 2}" : score > MATE - MAX_PLY ? $"mate {(MATE - score + 1) / 2}" : $"cp {score}";

         StringBuilder sb = new();
         sb.Append($"info depth {depth} ");
         sb.Append($"score {scoreStr} ");
         sb.Append($"nodes {nodes} ");
         sb.Append($"nps {Math.Round((double)(1000 * nodes / Math.Max(elapsedMs, 1)), 0)} ");
         sb.Append($"hashfull {TTable.GetUsed()} ");
         sb.Append($"time {elapsedMs} ");
         sb.Append($"pv {ThreadInfo.GetPv()} ");

         Console.WriteLine(sb.ToString());
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int GetTotalNodes()
      {
         return Nodes;
      }

      public void Run()
      {
         ThreadInfo.ResetForSearch();
         Nodes = 0;

         int alpha = -INFINITY;
         int beta = INFINITY;
         int score = 0;
         bool mainThread = Thread.CurrentThread.Name == "Thread 0";

         // Iterative deepening
         for (int i = 1; i <= TimeManager.MaxDepth; i++)
         {
            int margin = ASP_Margin;

            // Use aspiration windows at higher depths
            if (i >= ASP_Min_Depth)
            {
               alpha = Math.Max(score - margin, -INFINITY);
               beta = Math.Min(score + margin, INFINITY);
            }

            while (true)
            {
               score = ThreadInfo.Score = NegaScout(alpha, beta, i, 0, false);

               if (TimeManager.Stopped)
               {
                  goto ReportBestMove;
               }

               if (score <= alpha)
               {
                  alpha = Math.Max(score - margin, -INFINITY);
                  beta = (alpha + beta) / 2;
               }
               else if (score >= beta)
               {
                  beta = Math.Min(score + margin, INFINITY);
               }
               else
               {
                  break;
               }

               margin += margin / 2;
            }

            if (mainThread)
            {
               PrintInfo(i);
            }

            if (TimeManager.LimitReached(true, Nodes))
            {
               goto ReportBestMove;
            }
         }

         ReportBestMove:
         if (mainThread)
         {
            Console.WriteLine($"bestmove {ThreadInfo.GetBestMove()}");
         }
      }

      private int NegaScout(int alpha, int beta, int depth, int ply, bool doNull)
      {
         if ((Nodes & 2047) == 0 && TimeManager.LimitReached(false, Nodes))
         {
            return 0;
         }

         ThreadInfo.InitPvLength(ply);

         if (ply >= MAX_PLY)
         {
            return 0;
         }

         if (ply > 0 && IsDraw())
         {
            return 0;
         }

         bool isPVNode = beta != alpha + 1;
         bool isRoot = ply == 0;

         if (depth <= 0)
         {
            return Quiescence(alpha, beta, ply, isPVNode);
         }

         bool ttValid = TTable.Probe(Board.Hash, ply, out TTEntry entry);
         ushort ttMove = 0;

         if (ttValid)
         {
            ttMove = entry.Move;

            if (!isPVNode && entry.Depth >= depth)
            {
               switch (entry.Flag)
               {
                  case HashFlag.Exact:
                     {
                        return entry.Score;
                     }
                  case HashFlag.Beta when entry.Score >= beta:
                     {
                        return entry.Score;
                     }
                  case HashFlag.Alpha when entry.Score <= alpha:
                     {
                        return entry.Score;
                     }
               }
            }
         }

         bool inCheck = Board.IsAttacked(Board.GetSquareByPiece(PieceType.King, Board.SideToMove), (int)Board.SideToMove ^ 1);
         int staticEval = Evaluation.Evaluate(Board);

         ThreadInfo.EvalStack[ply] = staticEval;

         bool improving = false;

         if (ply >= 2 && !inCheck)
         {
            improving = staticEval > ThreadInfo.EvalStack[ply - 2];
         }

         if (!isPVNode && !inCheck)
         {
            // Reverse futility pruning
            if (depth <= RFP_Max_Depth && staticEval - RFP_Margin * (depth - (improving ? 1 : 0)) >= beta)
            {
               return (staticEval + beta) / 2;
            }

            // Null move pruning
            // The last condition prevents NMP if the STM only has a king and pawns left
            if (doNull && depth >= NMP_Min_Depth && staticEval >= beta && (Board.ColorBoard(Board.SideToMove) & Board.NonPawnMaterial))
            {
               Board.MakeNullMove();
               int score = -NegaScout(-beta, -beta + 1, depth - 1 - (3 + depth / 6), ply + 1, false);
               Board.UnmakeNullMove();

               if (score >= beta)
               {
                  return score;
               }
            }
         }

         int bestScore = -INFINITY;
         Move bestMove = new();
         int b = beta;
         HashFlag flag = HashFlag.Alpha;
         int legalMoves = 0;
         Move[] quietMoves = new Move[100];
         int quietMovesCount = 0;

         // Internal iterative reduction
         if (depth >= IIR_Min_Depth && ttMove == 0)
         {
            depth--;
         }

         MovePicker moves = new(Board, ThreadInfo, ply, new(ttMove), false);

         while (moves.Next() is Move move)
         {
            bool isQuiet = !move.HasType(MoveType.Capture) && !move.HasType(MoveType.Promotion);

            if (isQuiet && !isRoot)
            {
               // Late move pruning
               if (depth <= LMP_Max_Depth && legalMoves > LMP_Min_Margin + depth * (improving ? 2 : 1))
               {
                  moves.NoisyOnly = true;
               }

               // Futility pruning
               if (depth <= FP_Max_Depth && legalMoves > 0 && staticEval + FP_Margin * depth < alpha)
               {
                  moves.NoisyOnly = true;
               }
            }

            // SEE pruning
            if (!isPVNode && moves.Stage > Stage.Noisy && !Board.SEE_GE(move, (isQuiet ? SEE_Quiet_Threshold : SEE_Noisy_Threshold) * depth))
            {
               continue;
            }

            if (!Board.MakeMove(move))
            {
               Board.UndoMove(move);
               continue;
            }

            Board.MoveStack[ply] = (move, Board.Squares[move.To]);
            Nodes += 1;
            legalMoves += 1;

            if (isQuiet && quietMovesCount < 100)
            {
               quietMoves[quietMovesCount++] = move;
            }

            int E = inCheck ? 1 : 0;
            int newDepth = depth - 1 + E;
            bool doLMR = depth > LMR_Min_Depth && legalMoves > LMR_Min_MoveLimit;

            if (doLMR)
            {
               int R = LMR_Reductions[isQuiet ? 0 : 1][depth][legalMoves];

               if (isPVNode)
               {
                  R--;
               }

               if (Board.InCheck)
               {
                  R--;
               }

               if (!improving)
               {
                  R++;
               }

               newDepth = Math.Clamp(newDepth - R, 1, newDepth);
            }

            // First move of leftmost nodes get searched with a full window (because b = beta)
            // Subsequent moves get searched with a null window (b = alpha + 1)
            int score = -NegaScout(-b, -alpha, newDepth, ply + 1, true);

            // If reduced search failed high, retry with normal depth
            if (doLMR && score > alpha)
            {
               score = -NegaScout(-b, -alpha, depth - 1 + E, ply + 1, true);
            }

            // After the first legal move, if the intial search (above) fails high or low, research with the full window
            if (score > alpha && score < beta && legalMoves > 1)
            {
               score = -NegaScout(-beta, -alpha, depth - 1 + E, ply + 1, true);
            }

            Board.UndoMove(move);

            if (score > bestScore)
            {
               bestScore = score;

               if (score > alpha)
               {
                  bestMove = move;
                  alpha = score;
                  flag = HashFlag.Exact;
                  ThreadInfo.UpdatePV(bestMove, ply);
               }
            }

            if (score >= beta)
            {
               flag = HashFlag.Beta;

               if (isQuiet)
               {
                  if (move != ThreadInfo.KillerMoves[ply][0])
                  {
                     ThreadInfo.KillerMoves[ply][1] = ThreadInfo.KillerMoves[ply][0];
                     ThreadInfo.KillerMoves[ply][0] = move;
                  }

                  int bonus = depth * depth;

                  ThreadInfo.UpdateHistory(Board.SideToMove, move, bonus);

                  if (!isRoot)
                  {
                     ThreadInfo.UpdateCountermove(Board.MoveStack[ply - 1].Move, move);
                     ThreadInfo.UpdateContHistory(Board.Squares[move.From], move, Board.MoveStack, ply, 1, bonus);
                     ThreadInfo.UpdateContHistory(Board.Squares[move.From], move, Board.MoveStack, ply, 2, bonus);
                  }

                  // Reduce history score for other quiet moves
                  for (int i = 0; i < quietMovesCount; i++)
                  {
                     if (quietMoves[i] == move)
                     {
                        continue;
                     }

                     ThreadInfo.UpdateHistory(Board.SideToMove, quietMoves[i], -bonus);

                     if (!isRoot)
                     {
                        ThreadInfo.UpdateContHistory(Board.Squares[quietMoves[i].From], quietMoves[i], Board.MoveStack, ply, 1, -bonus);
                        ThreadInfo.UpdateContHistory(Board.Squares[quietMoves[i].From], quietMoves[i], Board.MoveStack, ply, 2, -bonus);
                     }
                  }
               }

               break;
            }

            // Adjust null window
            b = alpha + 1;
         }

         if (legalMoves == 0)
         {
            return inCheck ? -MATE + ply : 0;
         }

         TTable.SaveEntry(Board.Hash, (byte)depth, ply, bestMove.GetEncoded(), bestScore, flag);

         return bestScore;
      }

      private int Quiescence(int alpha, int beta, int ply, bool isPVNode)
      {
         if ((Nodes & 2047) == 0 && TimeManager.LimitReached(false, Nodes))
         {
            return 0;
         }

         if (ply >= MAX_PLY)
         {
            return 0;
         }

         if (IsDraw())
         {
            return 0;
         }

         bool ttValid = TTable.Probe(Board.Hash, ply, out TTEntry entry);
         ushort ttMove = 0;

         if (ttValid)
         {
            ttMove = entry.Move;

            if (!isPVNode)
            {
               switch (entry.Flag)
               {
                  case HashFlag.Exact:
                     {
                        return entry.Score;
                     }
                  case HashFlag.Beta when entry.Score >= beta:
                     {
                        return entry.Score;
                     }
                  case HashFlag.Alpha when entry.Score <= alpha:
                     {
                        return entry.Score;
                     }
               }
            }
         }

         int bestScore = Evaluation.Evaluate(Board);

         if (bestScore >= beta)
         {
            return bestScore;
         }
         else if (bestScore > alpha)
         {
            alpha = bestScore;
         }

         Move bestMove = new();
         HashFlag flag = HashFlag.Alpha;
         MovePicker moves = new(Board, ThreadInfo, ply, new(ttMove), true);

         while (moves.Next() is Move move)
         {
            // Delta pruning
            if (((move.HasType(MoveType.Promotion) ? 1 : 0) * Evaluation.GetPieceValue(PieceType.Queen, Board)) + bestScore + Evaluation.GetPieceValue(Board.Squares[move.To].Type, Board) + 200 < alpha)
            {
               continue;
            }

            if (!Board.SEE_GE(move, -50))
            {
               continue;
            }

            if (!Board.MakeMove(move))
            {
               Board.UndoMove(move);
               continue;
            }

            Board.MoveStack[ply] = (move, Board.Squares[move.To]);
            Nodes += 1;

            int score = -Quiescence(-beta, -alpha, ply + 1, isPVNode);

            Board.UndoMove(move);

            if (score > bestScore)
            {
               bestScore = score;
               bestMove = move;
            }

            if (score > alpha)
            {
               alpha = score;
               flag = HashFlag.Exact;
            }

            if (score >= beta)
            {
               flag = HashFlag.Beta;
               break;
            }
         }

         TTable.SaveEntry(Board.Hash, 0, ply, bestMove.GetEncoded(), bestScore, flag);

         return bestScore;
      }

      public bool IsDraw()
      {
         return IsRepeated() || Board.Halfmoves >= 100 || Board.IsDrawn();
      }

      private bool IsRepeated()
      {
         if (Board.Halfmoves < 4 || Board.History.Count <= 1)
         {
            return false;
         }

         int last = Math.Max(Board.History.Count - Board.Halfmoves, 0);

         for (int i = Board.History.Count - 4; i >= last; i -= 2)
         {
            if (Board.History[i].Hash == Board.Hash)
            {
               return true;
            }
         }

         return false;
      }
   }
}
