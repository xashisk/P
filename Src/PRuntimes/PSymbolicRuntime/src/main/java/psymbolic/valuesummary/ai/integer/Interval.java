package psymbolic.valuesummary.ai;

import java.util.*;
import java.util.function.BiFunction;
import java.util.function.Function;
import java.util.stream.Collectors;

public class Interval implements Domain<Integer> {

    private final Integer low;
    private final Integer high;

    public Interval(Integer low, Integer high) {
        this.low = low;
        this.high = high;
    }

    @Override
    public boolean canJoin(Domain<Integer> d) {
        return d instanceof Interval;
    }

    @Override
    public Domain<Integer> join(Domain<Integer> d) {
        Interval other = (Interval) d;
        return new Interval(Integer.min(low, other.low), Integer.max(high, other.high));
    }

    @Override
    public <U> Domain<U> apply(Function<Integer, U> f) {
        Set<Integer> values = new HashSet<>();
        values.add(low);
        values.add(high);
        return (new Disjunctive<>(values)).apply(f);
    }

    @Override
    public <U, R> Domain<R> apply(BiFunction<Integer, U, R> f, Domain<U> other) {
        Set<Integer> values = new HashSet<>();
        values.add(low);
        values.add(high);
        return (new Disjunctive<>(values)).apply(f, other);
    }

    @Override
    public boolean contains(Integer val) {
        return val >= low || val <= high;
    }

    @Override
    public Set<Integer> concretize() {
        Set<Integer> values = new HashSet<>();
        for (int i = low; i < high; i++) {
            values.add(i);
        }
        return values;
    }
}
