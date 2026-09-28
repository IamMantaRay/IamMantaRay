FeatureScript 2716;
import(path : "onshape/std/geometry.fs", version : "2716.0");

/**
 * Liquid Capacity
 *
 * Measures how much liquid a container (cup, bottle, tank, ...) can hold.
 *
 * How it works:
 *   1. A box slightly larger than the container is built, from below the
 *      container's bottom up to the liquid level (measured along the "up"
 *      direction).
 *   2. The container is subtracted from that box. The box splits into pieces.
 *   3. Pieces that touch the box's sides or bottom are "outside air" and are
 *      discarded. Whatever remains is enclosed by the container walls, i.e.
 *      the liquid.
 *   4. The liquid volume is measured and reported (mL / L, optional mass,
 *      % of full capacity).
 *   5. Optionally ("Show fill steps") the liquid is sliced into colored bands,
 *      one per step of the cavity depth, so you can see how the container
 *      fills up, and a height -> volume fill table is printed to the console.
 *
 * Works for open containers (cups, bowls) and closed ones (bottles, tanks).
 * The container itself is never modified.
 */

export enum FillMode
{
    annotation { "Name" : "Full (to the brim)" }
    FULL,
    annotation { "Name" : "Percent of cavity depth" }
    PERCENT,
    annotation { "Name" : "Liquid level (from container bottom)" }
    LEVEL
}

const FILL_PERCENT_BOUNDS = { (unitless) : [0, 80, 100] } as RealBoundSpec;
const DENSITY_BOUNDS = { (unitless) : [0, 1, 100] } as RealBoundSpec;
const STEP_COUNT_BOUNDS = { (unitless) : [2, 10, 50] } as IntegerBoundSpec;

annotation { "Feature Type Name" : "Liquid Capacity",
             "Feature Name Template" : "Liquid capacity (#volume)",
             "Feature Type Description" : "Measures the liquid volume a container can hold" }
export const liquidCapacity = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Container", "Filter" : EntityType.BODY && BodyType.SOLID }
        definition.container is Query;

        annotation { "Name" : "Up direction (default: +Z)",
                     "Filter" : QueryFilterCompound.ALLOWS_AXIS || GeometryType.PLANE,
                     "MaxNumberOfPicks" : 1 }
        definition.upDirection is Query;

        annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipUp is boolean;

        annotation { "Name" : "Fill mode" }
        definition.fillMode is FillMode;

        if (definition.fillMode == FillMode.PERCENT)
        {
            annotation { "Name" : "Fill (% of cavity depth)" }
            isReal(definition.fillPercent, FILL_PERCENT_BOUNDS);
        }
        else if (definition.fillMode == FillMode.LEVEL)
        {
            annotation { "Name" : "Liquid level" }
            isLength(definition.fillLevel, NONNEGATIVE_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Liquid density (g/mL)" }
        isReal(definition.density, DENSITY_BOUNDS);

        annotation { "Name" : "Keep liquid body" }
        definition.keepLiquid is boolean;

        if (definition.keepLiquid)
        {
            annotation { "Name" : "Show fill steps" }
            definition.showSteps is boolean;

            if (definition.showSteps)
            {
                annotation { "Name" : "Number of steps" }
                isInteger(definition.stepCount, STEP_COUNT_BOUNDS);
            }
        }

        annotation { "Name" : "Store as variable" }
        definition.storeVariable is boolean;

        if (definition.storeVariable)
        {
            annotation { "Name" : "Variable name" }
            definition.variableName is string;
        }
    }
    {
        if (isQueryEmpty(context, definition.container))
            throw regenError("Select a container body.", ["container"]);

        const container = qBodyType(definition.container, BodyType.SOLID);
        const up = getUpDirection(context, definition);
        const cSys = coordSystem(WORLD_ORIGIN, perpendicularVector(up), up);

        // Container bounds in the "up" coordinate system (z = up).
        const bounds = evBox3d(context, { "topology" : container, "cSys" : cSys, "tight" : true });
        const top = bounds.maxCorner[2];
        const containerHeight = top - bounds.minCorner[2];
        const margin = max(1 * millimeter, 0.02 * norm(bounds.maxCorner - bounds.minCorner));

        // Every solid this feature creates from here on is liquid (air pieces are deleted as they appear).
        const bodiesBefore = evaluateQuery(context, qEverything(EntityType.BODY));
        const liquidBodies = qBodyType(qSubtraction(qEverything(EntityType.BODY), qUnion(bodiesBefore)), BodyType.SOLID);

        // Pass 1: fill to the brim -> full capacity and cavity depth.
        var liquid = buildLiquid(context, id + "full", container, cSys, bounds, top, margin);
        var capacity = undefined;
        var cavity = undefined;
        if (!isQueryEmpty(context, liquid))
        {
            capacity = evVolume(context, { "entities" : liquid });
            cavity = evBox3d(context, { "topology" : liquid, "cSys" : cSys, "tight" : true });
        }
        else if (definition.fillMode != FillMode.LEVEL)
        {
            throw regenError("No enclosed volume found at the brim. Check the up direction, " ~
                             "or the container may overflow below its top (spout/handle higher than the rim) - " ~
                             "use 'Liquid level' mode in that case.", ["container", "upDirection"]);
        }

        // Pass 2: partial fill.
        var level = top;
        if (definition.fillMode == FillMode.PERCENT)
        {
            if (definition.fillPercent <= 0)
                throw regenError("Fill percentage must be greater than 0.", ["fillPercent"]);
            level = cavity.minCorner[2] + (definition.fillPercent / 100) * (cavity.maxCorner[2] - cavity.minCorner[2]);
        }
        else if (definition.fillMode == FillMode.LEVEL)
        {
            level = bounds.minCorner[2] + definition.fillLevel;
            if (definition.fillLevel > containerHeight)
            {
                reportFeatureWarning(context, id, "Liquid level exceeds container height; clamped to the top.");
                level = top;
            }
        }

        const partialFill = (definition.fillMode == FillMode.PERCENT && definition.fillPercent < 100) ||
                            (definition.fillMode == FillMode.LEVEL && level < top);
        if (partialFill)
        {
            if (!isQueryEmpty(context, liquid))
                opDeleteBodies(context, id + "deleteFull", { "entities" : liquid });
            liquid = buildLiquid(context, id + "partial", container, cSys, bounds, level, margin);
        }

        if (isQueryEmpty(context, liquid))
        {
            throw regenError("No liquid found at this level. The level may be below the inner bottom, " ~
                             "or the container leaks below it.", ["container"]);
        }

        // Measure.
        const volume = evVolume(context, { "entities" : liquid });
        const mL = volume / (centimeter ^ 3);
        const massG = mL * definition.density;
        const pieceCount = size(evaluateQuery(context, liquid));
        const liquidBox = evBox3d(context, { "topology" : liquid, "cSys" : cSys, "tight" : true });

        var message = "Liquid volume: " ~ fmt(mL, 2) ~ " mL (" ~ fmt(mL / 1000, 4) ~ " L), mass: " ~ fmt(massG, 2) ~ " g";
        if (capacity != undefined && partialFill)
        {
            message = message ~ ", " ~ fmt(100 * volume / capacity, 1) ~ "% of capacity (" ~
                      fmt(capacity / (centimeter ^ 3), 2) ~ " mL)";
        }
        if (pieceCount > 1)
            message = message ~ " [" ~ toString(pieceCount) ~ " separate cavities]";

        println(message);
        reportFeatureInfo(context, id, message);
        setFeatureComputedParameter(context, id, { "name" : "volume", "value" : volume });

        if (definition.storeVariable)
        {
            setVariable(context, definition.variableName, volume);
        }

        // Keep the liquid as a translucent blue part for visual checking, or remove it.
        if (definition.keepLiquid)
        {
            setProperty(context, { "entities" : liquid, "propertyType" : PropertyType.NAME, "value" : "Liquid" });
            setProperty(context, { "entities" : liquid, "propertyType" : PropertyType.APPEARANCE,
                                   "value" : color(0.2, 0.55, 1.0, 0.45) });

            if (definition.showSteps)
            {
                // Steps are measured over the full cavity depth when known, so a 60% fill shows 6 of 10 bands.
                const base = cavity != undefined ? cavity.minCorner[2] : liquidBox.minCorner[2];
                const span = cavity != undefined ? cavity.maxCorner[2] - base : liquidBox.maxCorner[2] - base;
                showFillSteps(context, id + "steps", liquidBodies, cSys, bounds, base, span,
                              liquidBox.maxCorner[2], definition.stepCount, capacity);
            }
        }
        else
        {
            opDeleteBodies(context, id + "deleteLiquid", { "entities" : liquid });
        }
    }, {
        "fillMode" : FillMode.FULL,
        "fillPercent" : 80,
        "fillLevel" : 0 * meter,
        "density" : 1,
        "flipUp" : false,
        "keepLiquid" : true,
        "showSteps" : false,
        "stepCount" : 10,
        "storeVariable" : false,
        "variableName" : "liquidVolume"
    });

/**
 * Slices the liquid into horizontal bands (one per step of the cavity depth), colors them
 * from light (bottom) to deep blue (surface), and prints a fill table: height -> cumulative volume.
 */
function showFillSteps(context is Context, id is Id, liquidBodies is Query, cSys is CoordSystem, bounds is Box3d,
    base is ValueWithUnits, span is ValueWithUnits, surface is ValueWithUnits, stepCount is number, capacity)
{
    const stepHeight = span / stepCount;
    const center = (bounds.minCorner + bounds.maxCorner) / 2;
    const planeSize = 3 * norm(bounds.maxCorner - bounds.minCorner);

    // Cut the liquid at every step level below its surface.
    for (var k = 1; k < stepCount; k += 1)
    {
        const z = base + k * stepHeight;
        if (z >= surface - TOLERANCE.zeroLength * meter)
            break;

        const planeId = id + ("plane" ~ toString(k));
        opPlane(context, planeId, {
                    "plane" : plane(toWorld(cSys, vector(center[0], center[1], z)), cSys.zAxis, cSys.xAxis),
                    "width" : planeSize,
                    "height" : planeSize
                });
        // Split each body on its own so a plane missing one cavity doesn't block the others.
        const targets = evaluateQuery(context, liquidBodies);
        for (var j = 0; j < size(targets); j += 1)
        {
            try silent(opSplitPart(context, id + ("split" ~ toString(k) ~ "_" ~ toString(j)), {
                                "targets" : targets[j],
                                "tool" : qCreatedBy(planeId, EntityType.FACE),
                                "keepTools" : true
                            }));
        }
        const planeBody = qCreatedBy(planeId, EntityType.BODY);
        if (!isQueryEmpty(context, planeBody))
            opDeleteBodies(context, id + ("deletePlane" ~ toString(k)), { "entities" : planeBody });
    }

    // Color and name each band; collect volume per band.
    var bandVolume = makeArray(stepCount, 0 * meter ^ 3);
    for (var piece in evaluateQuery(context, liquidBodies))
    {
        const b = evBox3d(context, { "topology" : piece, "cSys" : cSys, "tight" : true });
        const mid = (b.minCorner[2] + b.maxCorner[2]) / 2;
        const band = min(max(floor((mid - base) / stepHeight), 0), stepCount - 1);
        bandVolume[band] += evVolume(context, { "entities" : piece });

        const t = stepCount > 1 ? band / (stepCount - 1) : 1;
        setProperty(context, { "entities" : piece, "propertyType" : PropertyType.APPEARANCE,
                               "value" : color(0.65 - 0.6 * t, 0.9 - 0.6 * t, 1.0 - 0.15 * t, 0.55) });
        setProperty(context, { "entities" : piece, "propertyType" : PropertyType.NAME,
                               "value" : "Liquid " ~ fmt(100 * band / stepCount, 0) ~ "-" ~
                                         fmt(100 * (band + 1) / stepCount, 0) ~ "%" });
    }

    // Fill table.
    println("Fill table (height from inner bottom | % of depth | cumulative volume" ~
            (capacity != undefined ? " | % of capacity)" : ")"));
    var cumulative = 0 * meter ^ 3;
    for (var k = 0; k < stepCount; k += 1)
    {
        if (bandVolume[k] == 0 * meter ^ 3)
            continue;
        cumulative += bandVolume[k];
        const height = min((k + 1) * stepHeight, surface - base);
        var row = "  " ~ fmt(height / millimeter, 1) ~ " mm | " ~ fmt(100 * height / span, 1) ~ "% | " ~
                  fmt(cumulative / (centimeter ^ 3), 2) ~ " mL";
        if (capacity != undefined)
            row = row ~ " | " ~ fmt(100 * cumulative / capacity, 1) ~ "%";
        println(row);
    }
}

function fmt(value is number, digits is number) returns string
{
    return toString(roundToPrecision(value, digits));
}

/**
 * Returns the unit "up" vector: from the selected plane normal / axis, or world +Z.
 */
function getUpDirection(context is Context, definition is map) returns Vector
{
    var up = vector(0, 0, 1);
    if (!isQueryEmpty(context, definition.upDirection))
    {
        const plane = try silent(evPlane(context, { "face" : definition.upDirection }));
        if (plane != undefined)
        {
            up = plane.normal;
        }
        else
        {
            const axis = try silent(evAxis(context, { "axis" : definition.upDirection }));
            if (axis == undefined)
                throw regenError("Could not determine a direction from the selection.", ["upDirection"]);
            up = axis.direction;
        }
    }
    if (definition.flipUp)
        up = -up;
    return normalize(up);
}

/**
 * Builds the liquid body (or bodies) that fill the container up to `level`
 * (a z coordinate in `cSys`). Returns a query for the liquid bodies; may be empty.
 */
function buildLiquid(context is Context, id is Id, container is Query, cSys is CoordSystem,
    bounds is Box3d, level is ValueWithUnits, margin is ValueWithUnits) returns Query
{
    const existing = evaluateQuery(context, qEverything(EntityType.BODY));

    // Box around the container (in local coordinates), from below its bottom up to the liquid level.
    const boxMin = vector(bounds.minCorner[0] - margin, bounds.minCorner[1] - margin, bounds.minCorner[2] - margin);
    const boxMax = vector(bounds.maxCorner[0] + margin, bounds.maxCorner[1] + margin, level);

    fCuboid(context, id + "box", { "corner1" : boxMin, "corner2" : boxMax });
    const box = qCreatedBy(id + "box", EntityType.BODY);
    opTransform(context, id + "place", { "bodies" : box, "transform" : toWorld(cSys) });

    // Carve the container out of the box; the box splits into air + liquid pieces.
    opBoolean(context, id + "carve", {
                "tools" : container,
                "targets" : box,
                "operationType" : BooleanOperationType.SUBTRACTION,
                "keepTools" : true
            });

    const pieces = evaluateQuery(context, qSubtraction(qEverything(EntityType.BODY), qUnion(existing)));

    // Liquid = pieces that don't touch the box's sides or bottom (those are outside air).
    const tol = margin / 2;
    var liquid = [];
    var air = [];
    for (var piece in pieces)
    {
        const b = evBox3d(context, { "topology" : piece, "cSys" : cSys, "tight" : true });
        const enclosed = b.minCorner[0] > boxMin[0] + tol && b.maxCorner[0] < boxMax[0] - tol &&
                         b.minCorner[1] > boxMin[1] + tol && b.maxCorner[1] < boxMax[1] - tol &&
                         b.minCorner[2] > boxMin[2] + tol;
        if (enclosed)
            liquid = append(liquid, piece);
        else
            air = append(air, piece);
    }

    if (size(air) > 0)
        opDeleteBodies(context, id + "deleteAir", { "entities" : qUnion(air) });

    return qUnion(liquid);
}
