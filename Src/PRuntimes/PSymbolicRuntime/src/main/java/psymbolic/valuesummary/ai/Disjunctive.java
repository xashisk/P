package psymbolic.valuesummary.ai;

import lombok.Getter;
import java.util.function.Function;
import java.util.function.BiFunction;
import java.util.*;
import java.util.stream.Collectors;

public class Disjunctive<T> implements Domain<T> {

    @Getter
    private final Set<T> values;

    public Disjunctive (Collection<T> values) {
        this.values = new HashSet<>(values);
    }

    public Disjunctive (T value) {
        this.values = Collections.singleton(value);
    }

    @Override
    public boolean canJoin(Domain<T> d) {
        if (!(d instanceof Disjunctive<?>)) {
            return false;
        }
        Disjunctive<T> disj = (Disjunctive<T>) d;
        if (values.isEmpty() || disj.getValues().isEmpty()) {
            return true;
        }
        return values.iterator().next().getClass().equals(disj.getValues().iterator().next().getClass());
    }

    @Override
    public Domain<T> join(Domain<T> d) {
        Set<T> newValues = new HashSet<>(values);
        newValues.addAll(((Disjunctive<T>) d).getValues());
        return new Disjunctive<>(newValues);
    }

    @Override
    public <U> Domain<U> apply(Function<T, U> f) {
        return new Disjunctive<>(this.values.stream().map(f).collect(Collectors.toSet()));
    }

    @Override
    public <U, R> Domain<R> apply(BiFunction<T, U, R> f, Domain<U> other) {
        Domain<R> result = null;
        Set<Function<U, R>> fs = new HashSet<>();
        for (T value : values) {
            Domain<R> applyOther = other.apply(x -> f.apply(value, x));
            if (result == null) {
                result = applyOther;
            } else {
                result = result.join(applyOther);
            }
        }
        return result;
    }

    @Override
    public boolean contains(T val) {
        return this.values.contains(val);
    }

    @Override
    public Domain<Boolean> domainEquals(Domain<T> other) {
        if (other instanceof Disjunctive) {
            Set<Boolean> resultValues = new HashSet<>();
            for (T value : this.values) {
                if (((Disjunctive<T>) other).values.contains(value)) {
                    resultValues.add(true);
                } else {
                    resultValues.add(false);
                }
                if (resultValues.size() == 2) {
                    break;
                }
            }
            return DomainManager.fromConcrete(resultValues);
        }
        return DomainManager.fromConcrete(false);
    }

    @Override
    public Set<T> concretize() {
        return values;
    }

    @Override
    public String toString() {
        return "Disjunctive of " + getValues().toString();
    }

}
