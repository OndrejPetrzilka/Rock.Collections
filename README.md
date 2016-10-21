# Rock.Collections
Repository of collections which are missing in System.Collections.Generic.

## `OrderedHashSet<T>`
* Items are kept in order in which they are added
* Items can be reordered by methods: MoveFirst, MoveLast, MoveBefore, MoveAfter with complexity O(1)
* Items can be enumerated in reversed order
* Items can be enumerated from specified element to end or to beginning of the collection
* Based on .NET Core source code, added two ints to each slot as link to prev/next slot
* Memory overhead: 8 B per collection + 8 B per item (compared to HashSet)
* Performance overhead: Add/Remove 15-20% slower (compared to HashSet)
* Operation complexity: Same as HashSet, Add/Remove/Contains: O(1)

## `OrderedDictionary<TKey,TValue>`
* Same as `OrderedHashSet<T>` in almost every aspect

## How to use
* Download the files and add it to your project
* Or download whole shared project and that to your project

## Future
* SortedSet, like standard [System.Collections.Generic.SortedSet](https://msdn.microsoft.com/en-us/library/dd412070), but enumeration without allocations
* SortedDictionary, like standard [System.Collections.Generic.SortedDictionary](https://msdn.microsoft.com/en-us/library/dd412070), but enumeration without allocations

## Contributions
* Contributions to Unit Tests are welcome, current state of tests is awful.
* Bug fixes and improvements are welcome.
* New collection contributions should be consulted first (create feature request [issue](https://github.com/OndrejPetrzilka/Rock.Collections/issues))
