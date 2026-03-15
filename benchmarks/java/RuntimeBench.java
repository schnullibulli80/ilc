public final class RuntimeBench {
    private static final class Counter {
        private int value;

        private Counter(int seed) {
            this.value = seed;
        }

        private int tick(int delta) {
            value += delta;
            return value;
        }
    }

    private static int runLoop(int iterations) {
        int sum = 0;
        for (int index = 0; index < iterations; index++) {
            sum += index;
        }
        return sum;
    }

    private static int runArray(int iterations) {
        int[] values = new int[iterations];
        for (int index = 0; index < iterations; index++) {
            values[index] = index;
        }

        int sum = 0;
        for (int index = 0; index < iterations; index++) {
            sum += values[index];
        }
        return sum;
    }

    private static int runArrayFill(int iterations) {
        int[] values = new int[iterations];
        for (int index = 0; index < iterations; index++) {
            values[index] = index;
        }
        return values[iterations - 1];
    }

    private static int runArraySum(int iterations) {
        int[] values = new int[iterations];
        for (int index = 0; index < iterations; index++) {
            values[index] = index;
        }

        int sum = 0;
        for (int index = 0; index < iterations; index++) {
            sum += values[index];
        }
        return sum;
    }

    private static int runDispatch(int iterations) {
        Counter counter = new Counter(0);
        int sum = 0;
        for (int index = 0; index < iterations; index++) {
            sum += counter.tick(1);
        }
        return sum;
    }

    private static int runDispatchCall(int iterations) {
        Counter counter = new Counter(0);
        int current = 0;
        for (int index = 0; index < iterations; index++) {
            current = counter.tick(1);
        }
        return current;
    }

    private static int runDispatchAccumulate(int iterations) {
        Counter counter = new Counter(0);
        int sum = 0;
        for (int index = 0; index < iterations; index++) {
            sum += counter.tick(1);
        }
        return sum;
    }

    public static void main(String[] args) {
        if (args.length != 2) {
            System.out.println("ERROR=usage RuntimeBench <bench> <iterations>");
            System.exit(1);
            return;
        }

        String bench = args[0];
        int iterations = Integer.parseInt(args[1]);
        long startNs = System.nanoTime();
        int result;

        switch (bench) {
            case "loop":
                result = runLoop(iterations);
                break;
            case "array":
                result = runArray(iterations);
                break;
            case "array_fill":
                result = runArrayFill(iterations);
                break;
            case "array_sum":
                result = runArraySum(iterations);
                break;
            case "dispatch":
                result = runDispatch(iterations);
                break;
            case "dispatch_call":
                result = runDispatchCall(iterations);
                break;
            case "dispatch_accumulate":
                result = runDispatchAccumulate(iterations);
                break;
            default:
                System.out.println("ERROR=unknown benchmark " + bench);
                System.exit(1);
                return;
        }

        long elapsedMs = (System.nanoTime() - startNs) / 1_000_000L;
        System.out.println("BENCH=" + bench);
        System.out.println("ITERATIONS=" + iterations);
        System.out.println("ELAPSED_MS=" + elapsedMs);
        System.out.println("RESULT=" + result);
    }
}
