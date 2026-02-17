# VSLikeDoking.Demo

`VSLikeDoking.Demo` is a **usage guide sample** for developers who consume the `VsLikeDoking` DLL.

## Purpose

- Show the recommended initialization flow.
- Demonstrate basic content seeding (Document / ToolWindow).
- Demonstrate common API usage from menu actions.
- Provide a practical starting point for your own host application.

## Quick Start Flow

1. Run the demo.
2. Use **Init → Initialize() [quick start]**.
3. Use **Init → Initialize + Default Layout + Seed Content**.
4. Use **Actions** menu to add/move/close tabs.
5. Use **AutoHide** menu to test pin/show/hide scenarios.

## Recommended Integration Pattern

In your own app:

1. Create `DockManager` with your `IDockContentFactory`.
2. Create `VsDockRenderer`.
3. Call `DockHostControl.Initialize(manager, renderer)`.
4. Apply layout (`DefaultLayout` or your saved layout).
5. Seed initial content and activate a key.

The demo form `DockHostUsageGuideForm` follows the same pattern.
