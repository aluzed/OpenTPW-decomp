# T-042 — Economy controls: prices, admission fee, finances panel + loans

- **Priority**: 🟡 Feature
- **Type**: Engine / UI
- **Status**: ✅ Done — settable ride prices + admission fee, a loan system (take/repay, monthly
  instalments, bankruptcy flag), HUD readout, the **F11 finance history graph** (T-049), a per-ride price
  panel, and **one clickable button per loan offer** (Small/Large) are all in and verified.
- **Parent**: [T-038](T-038-park-management-ui.md). **Needs**: [T-040](T-040-build-mode-foundation.md).

## Done

- **Ride price** (`Ride.TicketPrice` now settable, default from excitement): click a ride in the
  Default tool to select it, `,`/`.` adjust its price; peeps pay it on boarding.
- **Admission fee** (`ParkFinances.EntryFee` settable): `[`/`]` adjust the gate fee.
- **Loans** (`ParkFinances.Loan`): two offers (principal + APR); `L` takes the small loan (cash in,
  outstanding = principal × (1+APR), 12 monthly instalments), `K` repays it in full. `Tick(dt)` debits
  monthly instalments (one "month" = 8 s) and sets the **Bankrupt** flag below −5000.
- **HUD** (`ParkStatsPanel`): ADMISSION, DEBT/LOAN, selected RIDE + price, and a BANKRUPT line.
- **Verified in-game**: `L` → DEBT 5500 (5000 × 1.10) + money +5000; over ~10 s the monthly instalments
  dropped DEBT 5500 → 3208. Admission/price use the same proven key-binding path as the loan key.

## Remaining (follow-up)

- ~~A clickable finance **panel**~~ — done: `FinancePanel` + the **F11 balance history graph** (T-049).
- ~~per-ride **price panel**~~ — done: `ManagePanel` has per-ride `PRICE-`/`PRICE+` when a ride is selected.
- ~~**loan UI to pick which offer**~~ — **done**: `ManagePanel` now renders **one button per loan offer**
  (`LOAN $5k` / `LOAN $15k`, the principal distinguishing the Small/Large offer), each taking or repaying its
  offer independently by index — previously only the first (`TakeLoan(0)`) was reachable. Unit-tested
  (`EachLoanOfferTakenAndRepaidIndependently`), verified on-screen.

Nothing actionable remains — the economy controls are complete.

## Context

`ParkFinances` tracks the balance read-only with derived prices/fee. This ticket gives the player
control: per-ride ticket price, the park admission fee, a proper finances panel, and the loan system.

## Reference (original)

`mCash`/`InitialCash`, `mAdmissionFee`/`InitialAdmissionFee` ("Admission fee set to %d"), and a full
loan model: `LoanInfo`, `LOANNAMES::TableID`, `mLoans[loan].amount_available` /`APR_in_percent`
/`monthly_repayment`/`loan_bought`, plus "Accepted for this loan", "Cannot afford to pay off loan",
"Congratulations! You've finished paying off loan %d!", "Bankrupted". So loans have an APR + monthly
repayment, can be bought/repaid, and bankruptcy is a real state. Initial values come from the level
config (`InitialCash`/`InitialAdmissionFee`).

## Work

1. **Ride price**: select a ride (T-040) → panel to set its ticket price; peeps pay that on boarding
   (already wired via `Ride.TicketPrice`, make it settable).
2. **Admission fee**: a park panel to set `EntryFee`; seed from the level config (`InitialAdmissionFee`).
3. **Finances panel**: balance + income/cost breakdown (extend the dev `ParkStatsPanel`) with a
   running history/graph.
4. **Loans**: model `Loan { amount, APR, monthlyRepayment, bought }`; take/repay loans, monthly
   interest debit, and a **bankruptcy** state when cash stays negative.

## Acceptance criteria

- Setting a ride's price changes ticket revenue; setting the admission fee changes gate income; a loan
  adds cash and debits monthly repayments; sustained negative cash triggers bankruptcy.

## Affected files

`source/OpenTPW/World/ParkFinances.cs`, `source/OpenTPW/World/Rides/Ride.cs`,
`source/OpenTPW/UI/Widgets/ParkStatsPanel.cs` (+ new finance/loan panels), `Level.cs`.

## 2026-10-21 — the level's own loans + opening budget

The two invented loans (Small $5k @10%, Large $15k @18%, 12 months) are replaced by the level's eight `LoanInfo[*]`
offers ($10k–$100k, 18–23% APR, 24–48 months) from `levels/Standard.sam`, the level's overrides applied (jungle
renames four lenders), lender names from `LOANNAMES.str` ("Cash Unlimited", "Inca Finance"…); repaid with simple APR
interest over the offer's own period. They're listed in a **LoanPanel** (L or the LOANS button) with TAKE / REPAY per
offer. The park also opens with `BankAccountInfo.InitialCash` (50 000) and `InitialAdmissionFee` (20) instead of the
hard-coded 30 000 / 10; as that fee equals `PeepInfo.MinimumEntryFee`, the "pays up happily" band is now inclusive.
Saves made before this name their loans "Small"/"Large", which no longer match an offer (the loan is dropped on load).
Unit-tested (`LoanOffersTests`); verified in-game.

## 2026-10-21 — rides are free (as in TPW)

The original's ride window (UITEXT 17–31: users, age, excitement, reliability, state of repair, scrap value,
upgrades) has no price; only shops ("Sale price") and sideshows ("Price of game") charge, and the gate fee's ideal is
derived from the park's total ride excitement. OpenTPW charged a per-ride ticket (excitement/10). Rides are now free:
`Ride.TicketPrice` is 0 except for sideshows, the ride PRICE± buttons / `,` `.` keys apply to sideshows only, and a
free ride has no value-for-money term in `Peep.RateRide`. A 130 s autoplace run still gains money (gate + shops).

## 2026-10-21 — stalls from their data

Each stall reads its `.sam` (`ShopBuilding.StatsFor`): name (`Info.Name`, e.g. "Burger Shop"), sale price
(`UsageInfo.InitPricePerUse`, burger 30), cost of goods (`UsageInfo.InitCostOfGoods`, 20 — deducted from each sale;
`ParkFinances.ShopProfit` = takings − goods, used by challenge type 12) and build cost (`Upgrades[0].CostOfUpgrade`:
food 800, drink 650, toilet 100 — were invented 500/450/400). Toilets stay free to use. Unit-tested.
