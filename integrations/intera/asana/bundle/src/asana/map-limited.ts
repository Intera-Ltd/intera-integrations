/**
 * Runs `task` over `items`, at most `limit` at a time, keeping the input order. A fan-out over a few thousand tasks
 * then holds `limit` promises rather than thousands; the client's per-token gate is what bounds requests in flight.
 */
export async function mapLimited<T, R>(items: T[], task: (item: T) => Promise<R>, limit = 16): Promise<R[]> {
  const results = new Array<R>(items.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const index = next++;
      results[index] = await task(items[index]);
    }
  });
  await Promise.all(workers);
  return results;
}
