# Test bots

Fake phone players for testing big games without rounding up 20 friends. Each bot is a separate headless
browser session, so the game treats them as different people. They join, type a dub, and vote at random.
Any JavaScript error on their phone page is printed.

Needs [Node.js](https://nodejs.org/) 18 or newer.

```
cd tools/test-bots
npm install
npm run setup
node bots.js ABCD 20 http://192.168.1.20:8080/
```

- `ABCD`: the room code on the TV.
- `20`: how many bots to add.
- The URL: the address shown on the TV. Leave it out to join through playwtd.com.

Stop with Ctrl+C.

The bots don't skip the intro. Only the first phone to join can do that, so join on your own phone first.
