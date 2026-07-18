import {
  ArrowLeft,
  ArrowClockwise,
  ArrowCounterClockwise,
  ArrowsClockwise,
  Check,
  Eye,
  EyeSlash,
  ImageSquare,
  Layout,
  LinkSimple,
  LockSimple,
  LockSimpleOpen,
  MagnifyingGlassMinus,
  MagnifyingGlassPlus,
  Plus,
  QrCode,
  Shapes,
  TextT,
} from '@phosphor-icons/react'
import {
  Canvas,
  FabricImage,
  Rect,
  Textbox,
} from 'fabric'
import type { FabricObject } from 'fabric'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { simulatorImages } from '../assets'
import { BrandMark, DemoMediaNotice, SimulatedPhoto } from '../components'

type EditorObject = FabricObject & {
  layerId?: string
  layerName?: string
  managedAssetId?: string
}

type LayerItem = {
  id: string
  name: string
  locked: boolean
  visible: boolean
}

type SelectionDetails = LayerItem & {
  left: number
  top: number
  width: number
  height: number
}

type EditorPanel = 'assets' | 'properties' | 'layers'
type DraftStatus = 'loading' | 'saved' | 'saving' | 'publishing' | 'published' | 'error'

let editorObjectSequence = 0

function nextLayerId() {
  editorObjectSequence += 1
  return `layer-${editorObjectSequence}`
}

function describeObject(object: EditorObject): SelectionDetails | null {
  if (!object.layerId || !object.layerName) {
    return null
  }

  return {
    id: object.layerId,
    name: object.layerName,
    locked: !object.selectable,
    visible: object.visible,
    left: Math.round(object.left),
    top: Math.round(object.top),
    width: Math.round(object.getScaledWidth()),
    height: Math.round(object.getScaledHeight()),
  }
}

function assignLayer(object: EditorObject, name: string, managedAssetId?: string) {
  object.layerId = nextLayerId()
  object.layerName = name
  object.managedAssetId = managedAssetId
  return object
}

function EditorPhoneReview() {
  return (
    <main id="main-content" className="editor-phone-review">
      <Layout aria-hidden="true" size={36} />
      <h1>Template review</h1>
      <p>Open Template Studio on a tablet or desktop to move objects and edit properties.</p>
      <div className="editor-phone-review__sheet" aria-label="Modern Strip paired output preview">
        {[0, 1].map((strip) => (
          <div className="editor-phone-review__strip" key={strip}>
            {simulatorImages.map((source, index) => (
              <SimulatedPhoto source={source} alt={`Synthetic photo ${index + 1} in strip preview`} key={source} />
            ))}
            <strong>Mara &amp; Nico</strong>
          </div>
        ))}
      </div>
      <dl>
        <div><dt>Output</dt><dd>Paired 2x6 on 4x6</dd></div>
        <div><dt>Draft</dt><dd>Cloud draft, unpublished</dd></div>
        <div><dt>Print safety</dt><dd>Safe area valid</dd></div>
      </dl>
      <Link className="button button--secondary" to="/portal">Return to operations</Link>
    </main>
  )
}

export function TemplateEditorPage() {
  const canvasElementRef = useRef<HTMLCanvasElement | null>(null)
  const fabricCanvasRef = useRef<Canvas | null>(null)
  const historyRef = useRef<string[]>([])
  const historyIndexRef = useRef(-1)
  const suppressHistoryRef = useRef(false)
  const saveTimerRef = useRef<number | null>(null)
  const [layers, setLayers] = useState<LayerItem[]>([])
  const [selection, setSelection] = useState<SelectionDetails | null>(null)
  const [historyIndex, setHistoryIndex] = useState(-1)
  const [draftStatus, setDraftStatus] = useState<DraftStatus>('loading')
  const [zoom, setZoom] = useState(1)
  const [previewMode, setPreviewMode] = useState(false)
  const [previewUrl, setPreviewUrl] = useState('')
  const [activePanel, setActivePanel] = useState<EditorPanel>('assets')

  const syncLayers = useCallback((canvas: Canvas) => {
    const nextLayers = canvas
      .getObjects()
      .map((object) => describeObject(object as EditorObject))
      .filter((object): object is SelectionDetails => object !== null)
      .reverse()
      .map(({ id, name, locked, visible }) => ({ id, name, locked, visible }))
    setLayers(nextLayers)

    const active = canvas.getActiveObject() as EditorObject | undefined
    setSelection(active ? describeObject(active) : null)
  }, [])

  const recordHistory = useCallback((canvas: Canvas) => {
    if (suppressHistoryRef.current) {
      return
    }

    const serialized = JSON.stringify(canvas.toDatalessJSON(['layerId', 'layerName', 'managedAssetId']))
    const retained = historyRef.current.slice(0, historyIndexRef.current + 1)
    retained.push(serialized)
    historyRef.current = retained.slice(-20)
    historyIndexRef.current = historyRef.current.length - 1
    setHistoryIndex(historyIndexRef.current)
  }, [])

  const queueDraftSave = useCallback(() => {
    setDraftStatus('saving')
    if (saveTimerRef.current !== null) {
      window.clearTimeout(saveTimerRef.current)
    }
    saveTimerRef.current = window.setTimeout(() => {
      setDraftStatus('saved')
      saveTimerRef.current = null
    }, 500)
  }, [])

  useEffect(() => {
    const canvasElement = canvasElementRef.current
    if (!canvasElement) {
      return
    }

    const abortController = new AbortController()
    const canvas = new Canvas(canvasElement, {
      width: 200,
      height: 600,
      backgroundColor: '#ffffff',
      preserveObjectStacking: true,
      selectionColor: 'oklch(0.82 0.16 113 / 0.18)',
      selectionBorderColor: 'oklch(0.72 0.16 113)',
      selectionLineWidth: 1,
    })
    fabricCanvasRef.current = canvas

    const selectionDisposer = canvas.on({
      'selection:created': () => syncLayers(canvas),
      'selection:updated': () => syncLayers(canvas),
      'selection:cleared': () => setSelection(null),
      'object:modified': () => {
        syncLayers(canvas)
        recordHistory(canvas)
        queueDraftSave()
      },
    })

    async function loadManagedImage(source: string) {
      try {
        return await FabricImage.fromURL(source, { crossOrigin: 'anonymous', signal: abortController.signal })
      } catch (error) {
        if (abortController.signal.aborted) {
          throw error
        }
        return FabricImage.fromURL(simulatorImages[0], { crossOrigin: 'anonymous', signal: abortController.signal })
      }
    }

    async function seedDraft() {
      const images = await Promise.all(simulatorImages.map((source) => loadManagedImage(source)))
      if (abortController.signal.aborted) {
        return
      }

      images.forEach((image, index) => {
        image.scaleToWidth(164)
        image.set({
          left: 18,
          top: 20 + index * 150,
          originX: 'left',
          originY: 'top',
          clipPath: new Rect({
            left: 18,
            top: 20 + index * 150,
            width: 164,
            height: 130,
            originX: 'left',
            originY: 'top',
            absolutePositioned: true,
          }),
          cornerColor: 'oklch(0.82 0.16 113)',
          borderColor: 'oklch(0.72 0.16 113)',
          transparentCorners: false,
        })
        assignLayer(image as EditorObject, `Photo ${index + 1}`, `simulator:capture-${String(index + 1).padStart(2, '0')}`)
        canvas.add(image)
      })

      const title = assignLayer(new Textbox('Mara & Nico', {
        left: 28,
        top: 486,
        width: 144,
        originX: 'left',
        originY: 'top',
        fontFamily: 'Manrope Variable',
        fontWeight: 700,
        fontSize: 18,
        textAlign: 'center',
        fill: '#1a1a18',
      }) as EditorObject, 'Event title')
      canvas.add(title)

      const date = assignLayer(new Textbox('JULY 19, 2026', {
        left: 40,
        top: 518,
        width: 120,
        originX: 'left',
        originY: 'top',
        fontFamily: 'IBM Plex Mono',
        fontSize: 8,
        charSpacing: 70,
        textAlign: 'center',
        fill: '#5d5d58',
      }) as EditorObject, 'Event date')
      canvas.add(date)

      ;[20, 170, 320].forEach((top) => {
        canvas.add(new Rect({
          left: 18,
          top,
          width: 164,
          height: 130,
          originX: 'left',
          originY: 'top',
          fill: 'transparent',
          stroke: 'oklch(0.72 0.16 113)',
          strokeWidth: 1,
          selectable: false,
          evented: false,
          excludeFromExport: true,
        }))
      })
      canvas.add(new Rect({
        left: 8,
        top: 8,
        width: 184,
        height: 584,
        originX: 'left',
        originY: 'top',
        fill: 'transparent',
        stroke: 'oklch(0.62 0.19 28)',
        strokeWidth: 1,
        strokeDashArray: [4, 4],
        selectable: false,
        evented: false,
        excludeFromExport: true,
      }))
      canvas.add(new Rect({
        left: 14,
        top: 14,
        width: 172,
        height: 572,
        originX: 'left',
        originY: 'top',
        fill: 'transparent',
        stroke: '#73736d',
        strokeWidth: 1,
        strokeDashArray: [7, 4],
        selectable: false,
        evented: false,
        excludeFromExport: true,
      }))
      canvas.requestRenderAll()
      syncLayers(canvas)
      recordHistory(canvas)
      setDraftStatus('saved')
    }

    void seedDraft().catch(() => {
      if (!abortController.signal.aborted) {
        setDraftStatus('error')
      }
    })

    return () => {
      abortController.abort()
      selectionDisposer()
      if (saveTimerRef.current !== null) {
        window.clearTimeout(saveTimerRef.current)
      }
      fabricCanvasRef.current = null
      void canvas.dispose()
    }
  }, [queueDraftSave, recordHistory, syncLayers])

  function findLayer(id: string) {
    return fabricCanvasRef.current
      ?.getObjects()
      .find((object) => (object as EditorObject).layerId === id) as EditorObject | undefined
  }

  function finishCanvasChange(canvas: Canvas, record = true) {
    canvas.requestRenderAll()
    syncLayers(canvas)
    if (record) {
      recordHistory(canvas)
    }
    queueDraftSave()
  }

  async function addManagedPhoto() {
    const canvas = fabricCanvasRef.current
    if (!canvas) {
      return
    }
    const image = await FabricImage.fromURL(simulatorImages[0], { crossOrigin: 'anonymous' })
    image.scaleToWidth(120)
    image.set({ left: 40, top: 200, originX: 'left', originY: 'top', cornerColor: 'oklch(0.82 0.16 113)', transparentCorners: false })
    assignLayer(image as EditorObject, `Photo ${layers.filter((layer) => layer.name.startsWith('Photo')).length + 1}`, 'simulator:capture-01')
    canvas.add(image)
    canvas.setActiveObject(image)
    finishCanvasChange(canvas)
  }

  function addText() {
    const canvas = fabricCanvasRef.current
    if (!canvas) {
      return
    }
    const text = assignLayer(new Textbox('Edit this text', {
      left: 30,
      top: 470,
      width: 140,
      originX: 'left',
      originY: 'top',
      fontFamily: 'Manrope Variable',
      fontSize: 16,
      fontWeight: 650,
      textAlign: 'center',
      fill: '#1a1a18',
    }) as EditorObject, 'Text')
    canvas.add(text)
    canvas.setActiveObject(text)
    finishCanvasChange(canvas)
  }

  function addShape() {
    const canvas = fabricCanvasRef.current
    if (!canvas) {
      return
    }
    const shape = assignLayer(new Rect({
      left: 50,
      top: 460,
      width: 100,
      height: 36,
      originX: 'left',
      originY: 'top',
      rx: 6,
      ry: 6,
      fill: 'oklch(0.82 0.16 113)',
      stroke: '#1a1a18',
      strokeWidth: 1,
    }) as EditorObject, 'Shape')
    canvas.add(shape)
    canvas.setActiveObject(shape)
    finishCanvasChange(canvas)
  }

  function addQrPlaceholder() {
    const canvas = fabricCanvasRef.current
    if (!canvas) {
      return
    }
    const placeholder = assignLayer(new Rect({
      left: 72,
      top: 500,
      width: 56,
      height: 56,
      originX: 'left',
      originY: 'top',
      fill: '#ffffff',
      stroke: '#1a1a18',
      strokeWidth: 2,
      strokeDashArray: [4, 3],
    }) as EditorObject, 'Gallery QR placeholder')
    canvas.add(placeholder)
    canvas.setActiveObject(placeholder)
    finishCanvasChange(canvas)
  }

  async function applyHistory(nextIndex: number) {
    const canvas = fabricCanvasRef.current
    const snapshot = historyRef.current[nextIndex]
    if (!canvas || !snapshot) {
      return
    }
    suppressHistoryRef.current = true
    await canvas.loadFromJSON(snapshot)
    canvas.requestRenderAll()
    suppressHistoryRef.current = false
    historyIndexRef.current = nextIndex
    setHistoryIndex(nextIndex)
    syncLayers(canvas)
    queueDraftSave()
  }

  function changeZoom(nextZoom: number) {
    const bounded = Math.min(1.25, Math.max(0.75, nextZoom))
    fabricCanvasRef.current?.setZoom(bounded)
    fabricCanvasRef.current?.requestRenderAll()
    setZoom(bounded)
  }

  function selectLayer(id: string) {
    const canvas = fabricCanvasRef.current
    const object = findLayer(id)
    if (!canvas || !object) {
      return
    }
    if (object.selectable) {
      canvas.setActiveObject(object)
    } else {
      canvas.discardActiveObject()
      setSelection(describeObject(object))
    }
    canvas.requestRenderAll()
    syncLayers(canvas)
    setActivePanel('properties')
  }

  function toggleLayerVisibility(id: string) {
    const canvas = fabricCanvasRef.current
    const object = findLayer(id)
    if (!canvas || !object) {
      return
    }
    object.set('visible', !object.visible)
    finishCanvasChange(canvas)
  }

  function toggleLayerLock(id: string) {
    const canvas = fabricCanvasRef.current
    const object = findLayer(id)
    if (!canvas || !object) {
      return
    }
    const locked = object.selectable
    object.set({
      selectable: !locked,
      evented: !locked,
      lockMovementX: locked,
      lockMovementY: locked,
      lockScalingX: locked,
      lockScalingY: locked,
      lockRotation: locked,
    })
    finishCanvasChange(canvas)
  }

  function updateSelectedPosition(axis: 'left' | 'top', value: number) {
    const canvas = fabricCanvasRef.current
    const object = selection ? findLayer(selection.id) : undefined
    if (!canvas || !object || Number.isNaN(value)) {
      return
    }
    object.set(axis, value)
    object.setCoords()
    canvas.requestRenderAll()
    setSelection(describeObject(object))
    queueDraftSave()
  }

  function commitSelectedChange() {
    const canvas = fabricCanvasRef.current
    if (canvas) {
      recordHistory(canvas)
      syncLayers(canvas)
    }
  }

  function togglePreview() {
    const canvas = fabricCanvasRef.current
    if (!canvas) {
      return
    }
    if (!previewMode) {
      canvas.discardActiveObject()
      canvas.requestRenderAll()
      setPreviewUrl(canvas.toDataURL({ format: 'png', multiplier: 1 }))
    }
    setPreviewMode((value) => !value)
  }

  function publishDraft() {
    setDraftStatus('publishing')
    window.setTimeout(() => setDraftStatus('published'), 700)
  }

  const statusCopy: Record<DraftStatus, string> = {
    loading: 'Loading managed assets',
    saved: 'Draft saved to cloud boundary',
    saving: 'Saving cloud draft',
    publishing: 'Publishing immutable version',
    published: 'Published as Modern Strip v4',
    error: 'Managed assets unavailable',
  }

  return (
    <div className={`editor editor--panel-${activePanel}${previewMode ? ' editor--preview' : ''}`}>
      <header className="editor-topbar">
        <div className="editor-topbar__identity">
          <Link className="icon-button" to="/portal" aria-label="Back to operations overview">
            <ArrowLeft aria-hidden="true" size={22} />
          </Link>
          <BrandMark compact />
          <span className="editor-breadcrumbs"><span>Templates</span><span>Mara &amp; Nico</span><strong>Modern Strip</strong></span>
        </div>
        <div className="editor-topbar__actions">
          <span className={`draft-status draft-status--${draftStatus}`} role="status">
            {draftStatus === 'saved' || draftStatus === 'published' ? <Check aria-hidden="true" size={16} weight="bold" /> : <ArrowsClockwise aria-hidden="true" size={16} />}
            {statusCopy[draftStatus]}
          </span>
          <button className="button button--secondary" type="button" onClick={togglePreview}>
            <Eye aria-hidden="true" size={19} />
            {previewMode ? 'Edit template' : 'Preview paired print'}
          </button>
          <button className="button button--secondary editor-save-button" type="button" onClick={queueDraftSave} disabled={draftStatus === 'saving'}>
            Save draft
          </button>
          <button className="button button--primary" type="button" onClick={publishDraft} disabled={draftStatus === 'publishing' || draftStatus === 'loading' || draftStatus === 'error'}>
            Publish
          </button>
        </div>
      </header>

      <EditorPhoneReview />

      <main id="main-content" className="editor-workbench">
        <div className="editor-panel-tabs" role="group" aria-label="Editor panel">
          {(['assets', 'properties', 'layers'] as const).map((panel) => (
            <button key={panel} type="button" aria-pressed={activePanel === panel} onClick={() => setActivePanel(panel)}>
              {panel}
            </button>
          ))}
        </div>

        <aside className="asset-rail editor-panel editor-panel--assets" aria-labelledby="asset-heading">
          <h2 id="asset-heading">Assets</h2>
          <div className="asset-tools">
            <button type="button" onClick={addManagedPhoto}><ImageSquare aria-hidden="true" size={26} /><span>Demo photo</span></button>
            <button type="button" onClick={addText}><TextT aria-hidden="true" size={26} /><span>Text</span></button>
            <button type="button" onClick={addShape}><Shapes aria-hidden="true" size={26} /><span>Shape</span></button>
            <button type="button" onClick={addQrPlaceholder}><QrCode aria-hidden="true" size={26} /><span>Gallery QR</span></button>
          </div>
          <div className="managed-assets">
            <h3>Managed assets</h3>
            <button type="button" onClick={addManagedPhoto}>
              <SimulatedPhoto source={simulatorImages[0]} alt="Synthetic managed asset thumbnail" />
              <span><strong>Event guests 01</strong><small>PNG, simulator fixture</small></span>
              <Plus aria-hidden="true" size={18} />
            </button>
            <DemoMediaNotice compact />
          </div>
        </aside>

        <section className="canvas-workspace" aria-labelledby="canvas-heading">
          <h1 id="canvas-heading" className="visually-hidden">Modern Strip template canvas</h1>
          <div className="canvas-toolbar">
            <div>
              <button className="icon-button" type="button" aria-label="Undo" title="Undo" disabled={historyIndex <= 0} onClick={() => void applyHistory(historyIndexRef.current - 1)}>
                <ArrowCounterClockwise aria-hidden="true" size={20} />
              </button>
              <button className="icon-button" type="button" aria-label="Redo" title="Redo" disabled={historyIndex >= historyRef.current.length - 1} onClick={() => void applyHistory(historyIndexRef.current + 1)}>
                <ArrowClockwise aria-hidden="true" size={20} />
              </button>
            </div>
            <div className="canvas-toolbar__zoom">
              <button className="icon-button" type="button" aria-label="Zoom out" onClick={() => changeZoom(zoom - 0.25)} disabled={zoom <= 0.75}>
                <MagnifyingGlassMinus aria-hidden="true" size={20} />
              </button>
              <output aria-label="Canvas zoom">{Math.round(zoom * 100)}%</output>
              <button className="icon-button" type="button" aria-label="Zoom in" onClick={() => changeZoom(zoom + 0.25)} disabled={zoom >= 1.25}>
                <MagnifyingGlassPlus aria-hidden="true" size={20} />
              </button>
            </div>
          </div>

          <div className="canvas-stage">
            {previewMode && previewUrl ? (
              <div className="paired-print-preview" aria-label="Paired 2x6 print preview">
                <img src={previewUrl} alt="Left 2x6 strip preview" />
                <img src={previewUrl} alt="Right duplicate 2x6 strip preview" />
              </div>
            ) : null}
            <div className={`fabric-canvas-frame${previewMode && previewUrl ? ' fabric-canvas-frame--hidden' : ''}`} aria-hidden={previewMode && previewUrl ? true : undefined}>
              <canvas ref={canvasElementRef} aria-label="Editable 2x6 template canvas" />
            </div>
            <div className="canvas-stage__status">
              <span><Check aria-hidden="true" size={18} weight="bold" /> Print safe</span>
              <span>2x6 strip at 300 DPI</span>
              <span><LinkSimple aria-hidden="true" size={17} /> Auto-duplicates on 4x6 print</span>
            </div>
          </div>
        </section>

        <aside className="properties-rail editor-panel editor-panel--properties" aria-labelledby="properties-heading">
          <h2 id="properties-heading">Properties</h2>
          {selection ? (
            <>
              <div className="selected-layer-heading">
                <span className="selected-layer-heading__swatch" />
                <strong>{selection.name}</strong>
                {selection.locked ? <LockSimple aria-label="Layer locked" size={18} /> : <LockSimpleOpen aria-label="Layer unlocked" size={18} />}
              </div>
              <fieldset className="property-grid">
                <legend>Position</legend>
                <label>X <input type="number" value={selection.left} onChange={(event) => updateSelectedPosition('left', Number(event.target.value))} onBlur={commitSelectedChange} /></label>
                <label>Y <input type="number" value={selection.top} onChange={(event) => updateSelectedPosition('top', Number(event.target.value))} onBlur={commitSelectedChange} /></label>
              </fieldset>
              <fieldset className="property-grid">
                <legend>Size</legend>
                <label>W <input type="number" value={selection.width} readOnly /></label>
                <label>H <input type="number" value={selection.height} readOnly /></label>
              </fieldset>
              <div className="property-actions">
                <button className="button button--secondary" type="button" onClick={() => toggleLayerLock(selection.id)}>
                  {selection.locked ? <LockSimpleOpen aria-hidden="true" size={18} /> : <LockSimple aria-hidden="true" size={18} />}
                  {selection.locked ? 'Unlock layer' : 'Lock layer'}
                </button>
                <button className="button button--secondary" type="button" onClick={() => toggleLayerVisibility(selection.id)}>
                  {selection.visible ? <EyeSlash aria-hidden="true" size={18} /> : <Eye aria-hidden="true" size={18} />}
                  {selection.visible ? 'Hide layer' : 'Show layer'}
                </button>
              </div>
            </>
          ) : (
            <div className="inspector-empty">
              <Layout aria-hidden="true" size={30} />
              <strong>Select a layer</strong>
              <p>Choose an item on the canvas or in Layers to edit its position and visibility.</p>
            </div>
          )}

          <section className="document-settings" aria-labelledby="document-heading">
            <h3 id="document-heading">Document</h3>
            <dl>
              <div><dt>Template</dt><dd>2x6 strip</dd></div>
              <div><dt>Output</dt><dd>600 x 1800 px</dd></div>
              <div><dt>Fabric</dt><dd>7.4.0</dd></div>
              <div><dt>Assets</dt><dd>Managed IDs</dd></div>
            </dl>
          </section>
        </aside>

        <aside className="layers-rail editor-panel editor-panel--layers" aria-labelledby="layers-heading">
          <div className="layers-rail__heading">
            <h2 id="layers-heading">Layers</h2>
            <span>{layers.length}</span>
          </div>
          <ol>
            {layers.map((layer) => (
              <li className={selection?.id === layer.id ? 'layer-row layer-row--selected' : 'layer-row'} key={layer.id}>
                <button className="layer-row__select" type="button" onClick={() => selectLayer(layer.id)}>
                  <ImageSquare aria-hidden="true" size={18} />
                  <span>{layer.name}</span>
                </button>
                <button className="layer-row__icon" type="button" aria-label={`${layer.locked ? 'Unlock' : 'Lock'} ${layer.name}`} onClick={() => toggleLayerLock(layer.id)}>
                  {layer.locked ? <LockSimple aria-hidden="true" size={17} /> : <LockSimpleOpen aria-hidden="true" size={17} />}
                </button>
                <button className="layer-row__icon" type="button" aria-label={`${layer.visible ? 'Hide' : 'Show'} ${layer.name}`} onClick={() => toggleLayerVisibility(layer.id)}>
                  {layer.visible ? <Eye aria-hidden="true" size={17} /> : <EyeSlash aria-hidden="true" size={17} />}
                </button>
              </li>
            ))}
          </ol>
          <div className="layer-guides">
            <span><i className="guide-line guide-line--bleed" /> Bleed</span>
            <span><i className="guide-line guide-line--safe" /> Safe area</span>
          </div>
        </aside>
      </main>
    </div>
  )
}
