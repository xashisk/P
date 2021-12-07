package psymbolic.valuesummary.ai;

import lombok.Getter;

import java.util.*;
import java.util.function.BiFunction;
import java.util.function.Function;
import java.util.stream.Collectors;

public class Interval<T extends Number & Comparable<T>> implements Domain<T> {

    @Getter
    private final T low;
    @Getter
    private final T high;

    public Interval(T value) {
        this.low = value;
        this.high = value;
    }

    public Interval(T low, T high) {
        this.low = low;
        this.high = high;
    }

    @Override
    public boolean canJoin(Domain<T> d) {
        return d instanceof Interval && ((Interval<?>) d).low.getClass().equals(this.low.getClass());
    }

    @Override
    public Domain<T> join(Domain<T> d) {
        Interval<T> other = (Interval<T>) d;
        T newLow = other.low;
        if (this.low.compareTo(other.low) < 0) {
            newLow = this.low;
        }
        T newHigh = other.high;
        if (this.high.compareTo(other.high) > 0) {
            newHigh = this.high;
        }
        return new Interval<>(newLow, newHigh);
    }

    @Override
    public <U> Domain<U> apply(Function<T, U> f) {
        Set<T> values = new HashSet<>();
        values.add(low);
        values.add(high);
        return (new Disjunctive<>(values)).apply(f);
    }

    @Override
    public <U, R> Domain<R> apply(BiFunction<T, U, R> f, Domain<U> other) {
        Set<T> values = new HashSet<>();
        values.add(low);
        values.add(high);
        return (new Disjunctive<>(values)).apply(f, other);
    }

    @Override
    public boolean contains(T val) {
        return (this.low.compareTo(val) <= 0) && (val.compareTo(this.high) <= 0);
    }

    @Override
    public Domain<Boolean> domainEquals(Domain<T> other) {
        if (other instanceof Interval) {
            Set<Boolean> resultValues = new HashSet<>();
            Interval<T> otherInterval = (Interval<T>) other;
            boolean lowerOverlap = otherInterval.high.compareTo(this.low) >= 0;
            boolean upperOverlap = this.high.compareTo(otherInterval.low) >= 0;
            resultValues.add(lowerOverlap);
            resultValues.add(upperOverlap);
            return DomainManager.fromConcrete(resultValues);
        }
        return DomainManager.fromConcrete(false);
    }

    @Override
    public T concretize() {
        if (this.low.equals(this.high)) return this.low;
        return null;
    }

}
