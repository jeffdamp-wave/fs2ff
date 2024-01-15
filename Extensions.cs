using System;
using System.Threading.Tasks;

namespace fs2ff
{
    /// <summary>
    /// Helper extension for some items
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Boxes the given value between the min and max values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static T AdjustToBounds<T>(this T value, T min, T max) where T : IComparable =>
            value.CompareTo(min) < 0
                ? min
                : value.CompareTo(max) > 0
                    ? max
                    : value;

        /// <summary>
        /// Task helper
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handler"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static async Task RaiseAsync<T>(this Func<T, Task>? handler, T value)
        {
            if (handler == null)
            {
                return;
            }

            Delegate[] delegates = handler.GetInvocationList();
            Task[] tasks = new Task[delegates.Length];

            for (var i = 0; i < delegates.Length; i++)
            {
                tasks[i] = ((Func<T, Task>)delegates[i])(value);
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Task helper
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="handler"></param>
        /// <param name="value1"></param>
        /// <param name="value2"></param>
        /// <returns></returns>
        public static async Task RaiseAsync<T1, T2>(this Func<T1, T2, Task>? handler, T1 value1, T2 value2)
        {
            if (handler == null)
            {
                return;
            }

            Delegate[] delegates = handler.GetInvocationList();
            Task[] tasks = new Task[delegates.Length];

            for (var i = 0; i < delegates.Length; i++)
            {
                tasks[i] = ((Func<T1, T2, Task>)delegates[i])(value1, value2);
            }

            await Task.WhenAll(tasks);
        }
    }
}
