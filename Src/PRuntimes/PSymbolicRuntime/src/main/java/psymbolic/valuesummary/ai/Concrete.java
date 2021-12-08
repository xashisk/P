package psymbolic.valuesummary.ai;

import lombok.Getter;

import java.util.Collections;
import java.util.Set;
import java.util.function.Function;
import java.util.function.BiFunction;

public class Concrete<T> implements Domain<T> {

    @Getter
    private final T value;

    public Concrete (T value) { this.value = value; }

    @Override
    public boolean canJoin(Domain<T> d) {
        return false;
    }

    @Override
    public Domain<T> join(Domain<T> d) {
        throw new RuntimeException("Illegal join of concrete domain.");
    }

    @Override
    public <U> Domain<U> apply(Function<T, U> f) {
        return new Concrete<U>(f.apply(this.value));
    }

    @Override
    public <U, R> Domain<R> apply(BiFunction<T, U, R> f, Domain<U> other) {
        return other.apply(x -> f.apply(this.value, x));
    }

    @Override
    public boolean contains(T val) {
        return this.value.equals(val);
    }

    @Override
    public Domain<Boolean> domainEquals(Domain<T> other) {
        return DomainManager.fromConcrete(this.value.equals(other.concretize()));
    }

    @Override
    public Set<T> concretize() { return Collections.singleton(value); }
}
