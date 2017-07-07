# Rock.Collections
Purpose of this repository is to provide some of high-performance collections, which are missing in System.Collections.Generic. The collections are written in way to minimize unnecessary memory allocations; they are originally used in game engine.

## `OrderedHashSet<T>`
* Items are kept in order in which they are added
* Items can be reordered by methods: `MoveFirst`, `MoveLast`, `MoveBefore`, `MoveAfter` with complexity O(1)
* Items can be enumerated in reversed order
* Items can be enumerated from specified element to end or to beginning of the collection
* Based on .NET Core source code, added two ints to each slot as link to prev/next slot
* Additional memory overhead: 8 B per collection + 8 B per item (compared to HashSet)
* Performance overhead: Add/Remove 15-20% slower (compared to HashSet)
* Operation complexity: Same as HashSet, Add/Remove/Contains are O(1)

## `OrderedDictionary<TKey,TValue>`
* Same as `OrderedHashSet<T>` in almost every aspect
* `bool Remove(TKey, out TValue)`, method which returns removed value

## `SortedSet<T>`
* Items are enumerated without allocations
* Items are added and removed without unnecessary allocations
* Items are stored in continuous array
* Manual iteration over collection is exposed through `FirstNode`, `LastNode`, `Node.Next` and `Node.Previous`
* Based on .NET Core source code, added parent reference to each node, reimplemented on array (it was on heap)
* Additional memory overhead: none, it uses less memory (not yet measured exactly)
* Performance overhead: Remove ~30% slower, Add ~20% faster when capacity is sufficient (compared to .NET)
* Operation complexity: Add/Remove/Contains are O(log2(n)), same as .NET SortedSet
<br>Clear operation is technically O(n), but very fast, it's only Array.Clear (same as HashSet for example)

## How to use
* Download the files and add it to your project
* Or download whole project and add that to your project

## Future
* ~~Range enumerators/readers for OrderedDictionary~~ done
* Range enumerators/readers for SortedSet
* SortedDictionary, like standard [System.Collections.Generic.SortedDictionary](https://msdn.microsoft.com/en-us/library/dd412070), but enumeration without allocations

## Contributions
* Contributions to Unit Tests are welcome, current state of tests is awful.
* Bug fixes and improvements are welcome.
* New collection contributions should be consulted first (create feature request [issue](https://github.com/OndrejPetrzilka/Rock.Collections/issues))
