## Inspiration

An average able-bodied person's grocery trip can take up to 45 minutes. For shoppers with disabilities, chronic pain, or limited stamina, walking for that long takes a mental and physical toll. Most people wander a grocery store, double back for forgotten items, and cross the store more than once. I wanted a tool that takes a grocery list and gives the shortest possible walk through the store, with clear guidance the whole way.

## Core loop

1. **Type your list.** Enter items in plain words. The app understands typos ("bannana"), plurals, and common names ("oj" for juice, "tp" for toilet paper), and tells you when the store doesn't carry something.
2. **Get the best route.** The app finds the order to visit the aisles that keeps your total walking distance as short as possible, then draws a glowing line on the floor with an arrow pointing the way.
3. **Follow and shop.** When you reach an aisle, a prompt asks if you want to pick up each item. Picked-up items are checked off your list and appear in your cart.
4. **Stay on track.** If you wander off the line, the route recalculates from where you are. You can also pick items up in any order, and the route adjusts.

## How I built it

- **Engine:** Unity, with the A* Pathfinding Project for walking paths, Feel for feedback effects, and Synty's POLYGON Shops assets for the store.
- **Item data:** I used the Instacart grocery dataset from Hugging Face, about 50,000 products across 134 categories. I mapped those categories to the demo store's 12 aisles and built a table of about 30,000 searchable item names, plus a list of common generic items and nicknames.
- **Real walking distances:** The app finds the walking path around the shelves between every pair of stops and records its length.
- **Finding the best order:** Choosing the order of stops is the traveling salesman problem, which I realized halfway through the project. Checking every possible order has a time complexity of O(n!). I used the Held-Karp algorithm, which is O(2ⁿ · n²). It records the shortest way to reach every combination of visited aisles and current position, so for 12 aisles it finds the guaranteed shortest route in about 600,000 steps instead of checking 479 million orders. For lists above 16 aisles, the app switches to the nearest neighbor method to build a good route quickly, then improves it with 2-opt and Or-opt.
- **Smooth guidance:** A* paths are made of straight lines and sharp corners, which look rigid for a person walking, so I replaced each corner with a curve to make the route feel natural. The sim tracks your progress along the route every frame, and the arrow always points along the path ahead of you, up to your next stop.

## Challenges I ran into

- **The arrow got confused.** Walking diagonally to the line made the arrow jump and point backward. I fixed it by using a dot product to project your position onto the route and measure progress as a distance that only moves forward, and by keeping the arrow from pointing past your next stop.
- **Higher difficulty than expected.** Although I practice LeetCode whenever I can, I didn't realize this was the traveling salesman problem until I started writing out the logic.

## Accomplishments that I'm proud of

- The route is mathematically guaranteed to be the shortest for any list in the demo store, and it recalculates instantly.
- The number of different products you can type in and get results for.
- The sim is polished and feels good to play.

## What I learned

- Dynamic programming.
- How much work real-world data needs before it's usable.

## What's next for Market Maps

- **Accessibility-aware routes:** Avoid narrow aisles, high shelves, and crowded areas based on each shopper's needs, using cameras in partner stores to detect crowds.
- **Audio-only guidance:** Spatial sound that guides shoppers with low vision along the route.
- **Voice input** for building the list hands-free.
- **Real store layouts,** so the route works in actual grocery stores.

## Built with

Unity, C#, A* Pathfinding Project, Feel, Synty POLYGON Shops, Python, Hugging Face (Instacart dataset)
