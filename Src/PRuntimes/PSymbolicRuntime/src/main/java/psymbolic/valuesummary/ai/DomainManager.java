package psymbolic.valuesummary.ai;

import psymbolic.runtime.Event;

import java.util.HashMap;
import java.util.Map;
import java.util.Set;
import java.util.function.Function;
import java.util.function.BiFunction;

public class DomainManager {

    public static NumberDomain numberDomain = NumberDomain.DISJUNCTIVE;
    public static DomainType genericDomain = DomainType.DISJUNCTIVE;

    public static <T> Domain<T> fromConcrete(T concrete) {
        return new Disjunctive<>(concrete);
    }

    public static <T> Domain<T> fromConcrete(Set<T> concrete) {
        return new Disjunctive<>(concrete);
    }

    public static <T> T toConcrete(Domain<T> d) {
        T result = d.concretize();
        if (result == null) throw new RuntimeException("Cannot concretize " + d);
        return result;
    }

    public static <T> boolean canJoin(Domain<T> d1, Domain<T> d2) {
        return d1.canJoin(d2);
    }

    public static <T> Domain<T> join(Domain<T> d1, Domain<T> d2) {
        return d1.join(d2);
    }

    public static <T, U> Domain<U> apply(Function<T, U> f, Domain<T> d) {
        return d.apply(f);
    }

    // TODO: figure out priority
    public static <T, U, R> Domain<R> apply(BiFunction<T, U, R> f, Domain<T> d1, Domain<U> d2) {
        return d1.apply(f, d2);
    }

    public static <T> boolean contains(Domain<T> d, T concrete) {
        return d.contains(concrete);
    }

    public static <T extends Number & Comparable<T>> T maxValue(Domain<T> d) {
        if (d instanceof Concrete) {
            return ((Concrete<T>) d).getValue();
        }
        if (d instanceof Interval) {
            return ((Interval<T>) d).getHigh();
        }
        if (d instanceof Disjunctive) {
            T max = null;
            Set<T> values = ((Disjunctive<T>) d).getValues();
            for (T val : values) {
                if (max == null) {
                    max = val;
                }
                if (val.compareTo(max) > 0) {
                    max = val;
                }
            }
            return max;
        }
        throw new RuntimeException("Tried to get maximum value in non-numerical domain.");
    }
}
