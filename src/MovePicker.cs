using System.Diagnostics;

namespace Puffin
{
   enum Stage
   {
      HashMove,
      GenNoisy,
      Noisy,
      Killers,
      Counter,
      GenQuiet,
      Quiet,
      BadNoisyInit,
      BadNoisy,
   }

   internal sealed class MovePicker(Board board, SearchInfo info, int ply, Move hashMove, bool noisyOnly)
   {
      private readonly Board Board = board;
      private readonly Move HashMove = hashMove;
      private readonly Move CounterMove = ply > 0 ? info.GetCountermove(board.MoveStack[ply - 1].Move) : default;
      private int Index = 0;
      private int BadNoisyIndex = 0;
      private int Killer = 0;
      private readonly SearchInfo SearchInfo = info;

      public bool NoisyOnly { get; set; } = noisyOnly;
      public Stage Stage { get; private set; } = Stage.HashMove;

      public Move? Next(ref MoveList MoveList)
      {
         switch (Stage)
         {
            case Stage.HashMove:
               {
                  Stage++;

                  if (Board.IsPseudoLegal(HashMove))
                  {
                     return HashMove;
                  }

                  goto case Stage.GenNoisy;
               }
            case Stage.GenNoisy:
               {
                  MoveGen.GenerateNoisy(ref MoveList, Board);
                  ScoreNoisyMoves(ref MoveList);
                  Stage++;
                  goto case Stage.Noisy;
               }
            case Stage.Noisy:
               {
                  while (Index < MoveList.Count)
                  {
                     Move move = NextMove(ref MoveList, Index++);

                     if (!Board.SEE_GE(move, 0))
                     {
                        MoveList.Add(move, MoveList.GetScore(Index - 1), BadNoisyIndex++);
                     }
                     else
                     {
                        return move;
                     }
                  }

                  if (NoisyOnly)
                  {
                     return null;
                  }

                  Stage++;
                  goto case Stage.Killers;
               }
            case Stage.Killers:
               {
                  while (Killer < 2)
                  {
                     if (Board.IsPseudoLegal(SearchInfo.KillerMoves[ply][Killer]))
                     {
                        return SearchInfo.KillerMoves[ply][Killer++];
                     }

                     Killer++;
                  }

                  Stage++;
                  goto case Stage.Counter;
               }
            case Stage.Counter:
               {
                  Stage++;

                  if (Board.IsPseudoLegal(CounterMove) && CounterMove != HashMove
                     && CounterMove != SearchInfo.KillerMoves[ply][0] && CounterMove != SearchInfo.KillerMoves[ply][1])
                  {
                     return CounterMove;
                  }

                  goto case Stage.GenQuiet;
               }
            case Stage.GenQuiet:
               {
                  if (!NoisyOnly)
                  {
                     MoveGen.GenerateQuiet(ref MoveList, Board);
                     ScoreQuietMoves(ref MoveList);
                  }
                  
                  Stage++;
                  goto case Stage.Quiet;
               }
            case Stage.Quiet:
               {
                  if (!NoisyOnly && Index < MoveList.Count)
                  {
                     return NextMove(ref MoveList, Index++);
                  }

                  Stage++;
                  goto case Stage.BadNoisyInit;
               }
            case Stage.BadNoisyInit:
               {
                  Index = 0;
                  Stage++;
                  goto case Stage.BadNoisy;
               }
            case Stage.BadNoisy:
               {
                  if (Index < BadNoisyIndex)
                  {
                     Debug.Assert(MoveList[Index].Flag != MoveFlag.Quiet);
                     return MoveList[Index++];
                  }

                  return null;
               }
            default:
               {
                  return null;
               }
         }
      }

      public static Move NextMove(ref MoveList list, int index)
      {
         // Selection sort
         int best = index;

         for (int i = index; i < list.Count; i++)
         {
            if (list.GetScore(i) > list.GetScore(best))
            {
               best = i;
            }
         }

         list.SwapMoves(index, best);

         return list[index];
      }

      private void ScoreNoisyMoves(ref MoveList moves)
      {
         for (int i = moves.Count - 1; i >= 0; i--)
         {
            Move move = moves[i];

            if (move == HashMove)
            {
               moves.RemoveAt(i);
               continue;
            }

            int baseScore = move.HasType(MoveType.Promotion)
               ? move.Flag == MoveFlag.QueenPromotion || move.Flag == MoveFlag.QueenPromotionCapture ? 250000 : -250000
               : 150000;
            PieceType captured = move.Flag == MoveFlag.EPCapture ? PieceType.Pawn : Board.Squares[move.To].Type;
            Piece moving = Board.Squares[move.From];

            moves.SetScore(i, baseScore + (50 * Constants.SEE_VALUES[(int)captured] - Constants.SEE_VALUES[(int)moving.Type]));
         }
      }

      private void ScoreQuietMoves(ref MoveList moves)
      {
         for (int i = moves.Count - 1; i >= 0; i--)
         {
            Move move = moves[i];

            if (move == HashMove || move == SearchInfo.KillerMoves[ply][0] || move == SearchInfo.KillerMoves[ply][1] || move == CounterMove)
            {
               moves.RemoveAt(i);
               continue;
            }

            moves.SetScore(i,
               SearchInfo.GetHistory(Board.Squares[move.From].Color, move)
               + SearchInfo.GetContHistory(Board.Squares[move.From], move, Board.MoveStack, ply, 1)
               + SearchInfo.GetContHistory(Board.Squares[move.From], move, Board.MoveStack, ply, 2)
            );
         }
      }
   }
}
