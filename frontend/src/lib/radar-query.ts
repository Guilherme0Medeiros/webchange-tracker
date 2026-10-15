import useSWR, { type ScopedMutator } from "swr";
import { get } from "@/lib/api";

export const REFRESH_INTERVAL = 30_000;

export function useRadarQuery<T>(path: string) {
  return useSWR<T, Error>(path, get, {
    refreshInterval: REFRESH_INTERVAL,
    refreshWhenHidden: false,
    revalidateOnFocus: true,
    focusThrottleInterval: 1_000,
    shouldRetryOnError: false,
  });
}

export function invalidatePaths(mutate: ScopedMutator, paths: string[]) {
  return mutate(key => typeof key === "string" && paths.some(path =>
    key === path || key.startsWith(`${path}?`)
  ));
}
