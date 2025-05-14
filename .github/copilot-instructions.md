## Project Coding Standards

### Comments
- Avoid stating the obvious.
- Only add comments when they add real value.
- Write all comments in English.

### Control Flow
- Minimize use of `if…else`; prefer guard clauses and early `return`.
- Avoid deep nesting of `if` statements; use early exits to keep methods flat.

### JSON Serialization
- Use `System.Text.Json` for all JSON (de)serialization.

### Try-Catch / Finally
- Avoid long `try…catch` or `try…finally` blocks (more than 3 lines) directly inside methods.
- For complex or multi-line error/finally logic, extract the `try…catch` and/or `finally` into a generic helper method.
- The helper should accept a delegate (`Action`, `Func<T>` or `Func<Task>`) and optional exception and/or finally handlers.
- In your methods, invoke this helper so that the method body remains focused on core logic.
