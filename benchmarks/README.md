# Benchmarks

Dieses Verzeichnis enthält einen ersten pragmatischen Vergleich zwischen:

- `ilcvm`
- `java`

Gemessen werden getrennt:

- Prozess-Startup
- Integer-Loop
- Array-Fill/Sum
- Objekt-/Methoden-Dispatch

Der Runner kompiliert beide Seiten frisch und führt anschließend:

- Startup-Messungen über externe Prozesszeit
- Workload-Messungen über die jeweils interne Monotonic-Clock

Ausführung:

```bash
/home/pgraf/repos/ilc/benchmarks/run-vm-vs-java.sh
```

Optional:

```bash
BENCH_ITERATIONS=2000000 BENCH_ROUNDS=5 BENCH_WARMUP=2 /home/pgraf/repos/ilc/benchmarks/run-vm-vs-java.sh
```

Wichtige Hinweise:

- `startup_ms` misst vor allem Tool-/Runtime-Startkosten.
- `bench_ms` misst nur die eigentliche Workload im Prozess.
- Java bekommt standardmäßig Warmup-Runden, damit der JIT nicht unfair benachteiligt wird.
- Das ist kein endgültiges Labor-Setup, aber gut genug für eine erste ehrliche Aussage `ilcvm vs JVM`.
