using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Providers.Plugins.Tmdb
{
    /// <summary>
    /// Helpers for consuming the sequences the TMDb client hands out.
    /// </summary>
    internal static class TmdbAsyncEnumerableExtensions
    {
        /// <summary>
        /// Ends the sequence when fetching more of it fails, keeping whatever it produced so far.
        /// A rate limited or failing request part way through a paged TMDb result should not throw
        /// away the pages that did arrive, so the failure is reported and the sequence simply ends.
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <param name="source">The sequence to guard.</param>
        /// <param name="onError">Called with the failure, before the sequence ends.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The items of the sequence, up to the first failure.</returns>
        public static async IAsyncEnumerable<T> StopOnError<T>(
            this IAsyncEnumerable<T> source,
            Action<Exception> onError,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var enumerator = source.GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    T current;

                    // Only the fetching is guarded: an exception cannot cross a yield return.
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }

                        current = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        onError(ex);
                        break;
                    }

                    yield return current;
                }
            }
        }
    }
}
