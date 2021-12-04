package psymbolic.valuesummary.ai;

import lombok.Getter;
import java.util.function.Function;
import java.util.function.BiFunction;
import java.util.*;
import java.util.stream.Collectors;

public class Disjunctive<T> implements Domain<T> {

    @Getter
    private final Set<T> values;

    Disjunctive (Collection<T> values) {
        this.values = new HashSet<>(values);
    }

    Disjunctive (T value) {
        this.values = Collections.singleton(value);
    }


    @Override
    public boolean canJoin(Domain d) {
        if (!(d instanceof Disjunctive<?>)) {
            return false;
        }
        Disjunctive disj = (Disjunctive<?>) d;
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
    public Set<T> concretize() {
        return values;
    }
}
