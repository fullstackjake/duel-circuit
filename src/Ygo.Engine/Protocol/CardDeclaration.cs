using System.Collections.Generic;
using System.Linq;

namespace Ygo.Engine.Protocol
{
    /// <summary>
    /// Evaluates the filter program sent with "declare a card name" prompts.
    /// Port of is_declarable() in ygopro-core playerop.cpp.
    /// </summary>
    public static class CardDeclaration
    {
        private const ulong Add = 0x4000000000000000, Sub = 0x4000000100000000, Mul = 0x4000000200000000,
            Div = 0x4000000300000000, And = 0x4000000400000000, Or = 0x4000000500000000,
            Neg = 0x4000000600000000, Not = 0x4000000700000000, BAnd = 0x4000000800000000,
            BOr = 0x4000000900000000, BNot = 0x4000001000000000, BXor = 0x4000001100000000,
            LShift = 0x4000001200000000, RShift = 0x4000001300000000, AllowAliases = 0x4000001400000000,
            AllowTokens = 0x4000001500000000, IsCode = 0x4000010000000000, IsSetCard = 0x4000010100000000,
            IsType = 0x4000010200000000, IsRace = 0x4000010300000000, IsAttribute = 0x4000010400000000,
            GetCode = 0x4000010500000000, GetTypeOp = 0x4000010700000000, GetRace = 0x4000010800000000,
            GetAttribute = 0x4000010900000000;

        public static bool IsDeclarable(CardInfo cd, IReadOnlyList<ulong> opcodes)
        {
            var stack = new Stack<long>();
            bool alias = false, token = false;

            void Binary(System.Func<long, long, long> op)
            {
                if (stack.Count < 2) return;
                var rhs = stack.Pop();
                var lhs = stack.Pop();
                stack.Push(op(lhs, rhs));
            }
            void Unary(System.Func<long, long> op)
            {
                if (stack.Count < 1) return;
                stack.Push(op(stack.Pop()));
            }

            foreach (var opcode in opcodes)
            {
                switch (opcode)
                {
                    case Add: Binary((a, b) => a + b); break;
                    case Sub: Binary((a, b) => a - b); break;
                    case Mul: Binary((a, b) => a * b); break;
                    case Div: Binary((a, b) => b == 0 ? 0 : a / b); break;
                    case And: Binary((a, b) => (a != 0 && b != 0) ? 1 : 0); break;
                    case Or: Binary((a, b) => (a != 0 || b != 0) ? 1 : 0); break;
                    case Neg: Unary(a => -a); break;
                    case Not: Unary(a => a == 0 ? 1 : 0); break;
                    case BAnd: Binary((a, b) => a & b); break;
                    case BOr: Binary((a, b) => a | b); break;
                    case BXor: Binary((a, b) => a ^ b); break;
                    case BNot: Unary(a => ~a); break;
                    case LShift: Binary((a, b) => a << (int)b); break;
                    case RShift: Binary((a, b) => a >> (int)b); break;
                    case IsCode: Unary(a => cd.Code == (uint)a ? 1 : 0); break;
                    case IsType: Unary(a => cd.Type & a); break;
                    case IsRace: Unary(a => (long)cd.Race & a); break;
                    case IsAttribute: Unary(a => cd.Attribute & a); break;
                    case GetCode: stack.Push(cd.Code); break;
                    case GetTypeOp: stack.Push(cd.Type); break;
                    case GetRace: stack.Push((long)cd.Race); break;
                    case GetAttribute: stack.Push(cd.Attribute); break;
                    case IsSetCard:
                        if (stack.Count >= 1)
                        {
                            var setCode = (int)stack.Pop();
                            var setType = setCode & 0xfff;
                            var setSubtype = setCode & 0xf000;
                            stack.Push(cd.Setcodes.Any(sc => (sc & 0xfff) == setType && (sc & 0xf000 & setSubtype) == setSubtype) ? 1 : 0);
                        }
                        break;
                    case AllowAliases: alias = true; break;
                    case AllowTokens: token = true; break;
                    default: stack.Push((long)opcode); break;
                }
            }

            if (stack.Count != 1 || stack.Peek() == 0) return false;
            bool isToken = (cd.Type & (CardType.Monster | CardType.Token)) == (CardType.Monster | CardType.Token);
            return (alias || cd.Alias == 0) && (token || !isToken);
        }
    }
}
