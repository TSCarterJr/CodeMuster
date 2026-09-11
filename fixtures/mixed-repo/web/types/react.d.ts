declare module "react" {
  export function useState<S>(initial: S | (() => S)): [S, (value: S | ((prev: S) => S)) => void];
  export function useEffect(effect: () => void | (() => void), deps?: readonly unknown[]): void;
}

declare namespace JSX {
  interface Element {}
  interface IntrinsicElements {
    [element: string]: Record<string, unknown>;
  }
}
