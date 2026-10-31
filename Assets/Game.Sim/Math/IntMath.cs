using System;

namespace Game.Sim;

// Integer-only helpers: the sim must give bit-identical results on every machine.
public static class IntMath
{
    // Floor of the square root, computed bit by bit so no hardware rounding is involved.
    public static long Sqrt(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Square root of a negative number.");

        ulong remainder = (ulong)value;
        ulong result = 0;
        ulong bit = 1UL << 62;
        while (bit > remainder)
            bit >>= 2;

        while (bit != 0)
        {
            if (remainder >= result + bit)
            {
                remainder -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return (long)result;
    }

    // Division rounding toward negative infinity; C# '/' truncates toward zero,
    // which would map milli-coordinate -1 into cell 0 instead of cell -1.
    public static int FloorDiv(int dividend, int divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException();

        int quotient = dividend / divisor;
        if ((dividend % divisor != 0) && ((dividend < 0) != (divisor < 0)))
            quotient--;
        return quotient;
    }
}
